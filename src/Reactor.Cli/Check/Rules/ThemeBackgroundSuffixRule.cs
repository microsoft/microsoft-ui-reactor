// Class A (induced) rule — originally authored as Class B and promoted to
// Class A on 2026-05-11 after the claude-sonnet-4.6 525-run corpus drop
// cleared the Validation Gate's cross-agent reproducibility bar (#2).
// Spec 038 §6, vocab table row "Theme.AppBackground / Theme.DefaultBackground
// / Theme.WindowBackground / Theme.PageBackground → Theme.SolidBackground".
//
// CS0117 on `Microsoft.UI.Reactor.Core.Theme` with a missing static member
// whose name ends in "Background". The agent reaches for an English-plausible
// XAML-shaped token (`AppBackground`, `DefaultBackground`); Reactor's canonical
// surface-background token is `Theme.SolidBackground` (which resolves to
// `SolidBackgroundFillColorBaseBrush`).
//
// Tier-2 fuzzy match can't bridge this: the closest real Theme member by
// JaroWinkler is `CardBackground`/`AccentBackground`, which is plausible
// but wrong; both corpora show the agent invariably reaches for
// SolidBackground in the eventual fix.
//
// Cross-agent reproducibility (Validation Gate bar #2): STRONG. 16+11 = 27
// events across the gpt-5.5 525-run and claude-sonnet-4.6 525-run corpora
// on the same (CS0117, Theme, other) key. Provenance is kept as
// `vocab:WinUI3` because the structural justification (XAML theme-resources
// doc citation in `docs/specs/tasks/038-vocab-table.csv`) still holds and
// is in many ways the more durable evidence than corpus frequency alone —
// the rule was authored from the vocab table first and the corpus later
// confirmed it. The Class-A → Class-B distinction is about evidence type,
// not rule shape; the rule itself is unchanged. Cross-agent audit recorded
// at `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md`.
//
// 2026-05-16 refinement — claude-opus-4.7 run5 (140-trial reactor sweep) cross-
// analysis at `OneDrive\big-eval\reactor-runs-cross-analysis.md` surfaced one
// `*Background`-suffixed invented name that is NOT a surface-background and
// must NOT collapse to SolidBackground:
//   • LayerBackground — 64 occurrences across 28 distinct trials (the single
//     highest-frequency invented Theme.* name in the corpus). The agent wants a
//     layer fill, mapping to `Theme.LayerFill` (`LayerFillColorDefaultBrush`),
//     not a surface background. The pre-refinement rule was actively wrong
//     here: it suggested SolidBackground for 28 trials. The override table
//     below short-circuits before the suffix fallback.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class ThemeBackgroundSuffixRule : IRulePattern
{
    const string ThemeFqn = "Microsoft.UI.Reactor.Core.Theme";
    const string SuffixFallbackTarget = "SolidBackground";
    const string BackgroundSuffix = "Background";

    // Exact-name overrides for `*Background` invented names that map to a real
    // Theme member OTHER than SolidBackground. Without these the suffix rule
    // would emit a confidently-wrong suggestion. Keys are intentional Ordinal:
    // we only override on exact agent-typed spellings observed in corpus.
    static readonly IReadOnlyDictionary<string, string> ExactOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // run5 (opus-4.7, 140 reactor trials): 64 occurrences across 28
            // distinct trials. The agent wants a layer fill brush — the real
            // member is Theme.LayerFill (LayerFillColorDefaultBrush). Single
            // strongest cross-trial signal in the run5 invented-name table.
            ["LayerBackground"] = "LayerFill",
        };

    public string Name => "ThemeBackgroundSuffixRule";
    public string Provenance => "vocab:WinUI3";
    public IReadOnlyList<string> DiagnosticCodes { get; } = new[] { "CS0117" };
    public IReadOnlyList<string> DeclaredTargets { get; } = new[] { ThemeFqn };

    public RuleSuggestion TryMatch(in RuleContext ctx)
    {
        if (ctx.Diagnostic.Id != "CS0117") return RuleSuggestion.Silent;
        if (ctx.Receiver is null) return RuleSuggestion.Silent;

        var themeType = ctx.Resolver.ResolveType(ThemeFqn);
        if (themeType is null) return RuleSuggestion.Silent;

        // Receiver must be `Theme` itself — not some unrelated user type that
        // happens to have a *Background member. SymbolEqualityComparer rules
        // out look-alikes from other namespaces.
        if (!SymbolEqualityComparer.Default.Equals(ctx.Receiver, themeType))
            return RuleSuggestion.Silent;

        var memberName = ExtractMissingName(ctx.Node);
        if (memberName is null) return RuleSuggestion.Silent;
        if (!memberName.EndsWith(BackgroundSuffix, StringComparison.Ordinal))
            return RuleSuggestion.Silent;

        // Pick exact-override target first, else fall through to the
        // surface-background default. Evidence string distinguishes the two
        // so reviewers can spot which path fired.
        var hasOverride = ExactOverrides.TryGetValue(memberName, out var overrideTarget);
        var target = hasOverride ? overrideTarget! : SuffixFallbackTarget;

        // Same name we'd propose? Then the diagnostic must be on a different
        // axis (compiler reordering, multitree edge case). Silent rather than
        // emit a no-op suggestion.
        if (string.Equals(memberName, target, StringComparison.Ordinal))
            return RuleSuggestion.Silent;

        // The chosen target must still exist on the current Reactor Theme
        // surface — protects against a rename that hasn't propagated.
        if (ctx.Resolver.ResolveMember(themeType, target) is null)
            return RuleSuggestion.Silent;

        var evidence = hasOverride
            ? $"Theme.{memberName} → Theme.{target} (run5 cross-trial override)"
            : $"Theme.{memberName} → Theme.{target} (WinUI surface-background token)";

        return new RuleSuggestion(
            Text: $"Theme.{target}",
            Confidence: 0.92,
            Evidence: evidence);
    }

    static string? ExtractMissingName(SyntaxNode node) => node switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax id => id.Identifier.ValueText,
        _ => null,
    };
}
