// Class A (induced) rule — corpus-justified by the run5 cross-analysis at
// `OneDrive\big-eval\reactor-runs-cross-analysis.md` (claude-opus-4.7,
// 140-trial reactor sweep, 2026-05-15).
//
// CS0117 on `Microsoft.UI.Reactor.Core.Theme` where the agent typed a name
// that looks like (or partially mirrors) the underlying WinUI 3 ThemeResource
// key — `LayerFillColorDefault`, `AccentFillColorDefault`, `SubtleFillColorSecondary`,
// `TextFillColorSecondary`, `CardBackgroundFillColorDefault`, plus the
// "Color"-stripped near-variants (`SubtleFillSecondary`, `AccentFill`).
// None of these exist as members on Reactor's `Theme` static — the canonical
// surface deliberately renames them to short, intent-named aliases:
//   • AccentFillColorDefaultBrush      → Theme.Accent
//   • LayerFillColorDefaultBrush       → Theme.LayerFill
//   • SubtleFillColorSecondaryBrush    → Theme.SubtleFill
//   • TextFillColorSecondaryBrush      → Theme.SecondaryText
//   • CardBackgroundFillColorDefaultBrush → Theme.CardBackground
//
// Why this is a SEPARATE rule from ThemeBackgroundSuffixRule rather than a
// fold-in: the failure shape is different. The suffix rule covers tokens that
// END IN "Background"; this rule covers tokens that EMBED the WinUI
// "<X>FillColor<Y>" stem. Composing them via overrides on one rule would
// require either two suffix checks or a leading regex, and `--disable-rule`
// granularity is more useful when each pattern family is its own kill switch.
//
// Tier-2 fuzzy match also can't bridge these: from `LayerFillColorDefault`
// JaroWinkler picks `LayerFill` as the closest member with similarity ≈ 0.72,
// which IS the right answer — but the per-code CS0117 threshold (0.75) is just
// above the actual similarity, so Tier-2 stays silent and Tier-3 inherits the
// hand-off. (Holding the Tier-2 threshold at 0.75 keeps wrong-suggestion rate
// down for the long-tail names; the high-frequency map in this file covers
// the names that matter without trading away threshold safety.)
//
// Cross-agent reproducibility (Validation Gate bar #2): PARTIAL. Authored
// from the run5 opus-4.7 corpus only — the entries are also justified by
// the underlying WinUI 3 ThemeResource key documentation (structural Class-B
// evidence), which is the more durable signal of the two. Re-validate when
// the next sonnet-4.6 / opus-4.7 sweep lands.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class ThemeRawResourceKeyRule : IRulePattern
{
    const string ThemeFqn = "Microsoft.UI.Reactor.Core.Theme";

    // Exact-name map. Each entry meets BOTH bars:
    //   • ≥ 2 distinct trials in run5 (cross-trial signal, not single-trial fluke)
    //   • Underlying WinUI 3 ThemeResource key maps unambiguously to a real
    //     member on the current Reactor Theme surface — independent of corpus.
    static readonly IReadOnlyDictionary<string, string> Mappings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LayerFillColorDefault"]          = "LayerFill",          // 6 trials
            ["SubtleFillColorSecondary"]       = "SubtleFill",         // 6 trials
            ["SubtleFillSecondary"]            = "SubtleFill",         // 12 trials (Color-stripped)
            ["AccentFillColorDefault"]         = "Accent",             // 3 trials
            ["AccentFill"]                     = "Accent",             // 3 trials (Color-stripped)
            ["CardBackgroundFillColorDefault"] = "CardBackground",     // 2 trials
            ["TextFillColorSecondary"]         = "SecondaryText",      // 2 trials
        };

    public string Name => "ThemeRawResourceKeyRule";
    public string Provenance => "cluster:run5-opus-theme-raw-keys";
    public IReadOnlyList<string> DiagnosticCodes { get; } = new[] { "CS0117" };
    public IReadOnlyList<string> DeclaredTargets { get; } = new[] { ThemeFqn };

    public RuleSuggestion TryMatch(in RuleContext ctx)
    {
        if (ctx.Diagnostic.Id != "CS0117") return RuleSuggestion.Silent;
        if (ctx.Receiver is null) return RuleSuggestion.Silent;

        var themeType = ctx.Resolver.ResolveType(ThemeFqn);
        if (themeType is null) return RuleSuggestion.Silent;

        // Receiver must be Reactor's `Theme` — same defensive symbol check as
        // ThemeBackgroundSuffixRule, for the same reason (look-alike namespaces).
        if (!SymbolEqualityComparer.Default.Equals(ctx.Receiver, themeType))
            return RuleSuggestion.Silent;

        var memberName = ExtractMissingName(ctx.Node);
        if (memberName is null) return RuleSuggestion.Silent;
        if (!Mappings.TryGetValue(memberName, out var target)) return RuleSuggestion.Silent;

        // Target must still exist on the current Reactor Theme surface — the
        // CI rule-target gate doesn't check value-side members, only the
        // DeclaredTargets type, so do the membership check here. If a future
        // rename moves `LayerFill` → `LayerSurface`, this rule self-skips
        // instead of emitting a stale suggestion.
        if (ctx.Resolver.ResolveMember(themeType, target) is null)
            return RuleSuggestion.Silent;

        return new RuleSuggestion(
            Text: $"Theme.{target}",
            Confidence: 0.92,
            Evidence: $"Theme.{memberName} → Theme.{target} (WinUI resource-key transliteration)");
    }

    static string? ExtractMissingName(SyntaxNode node) => node switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax id => id.Identifier.ValueText,
        _ => null,
    };
}
