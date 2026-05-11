// Class B vocabulary-translation rule. Spec 038 §6, vocab table row
// "Theme.AppBackground / Theme.DefaultBackground / Theme.WindowBackground /
// Theme.PageBackground → Theme.SolidBackground".
//
// CS0117 on `Microsoft.UI.Reactor.Core.Theme` with a missing static member
// whose name ends in "Background". The agent reaches for an English-plausible
// XAML-shaped token (`AppBackground`, `DefaultBackground`); Reactor's canonical
// surface-background token is `Theme.SolidBackground` (which resolves to
// `SolidBackgroundFillColorBaseBrush`).
//
// Class B authorship — Tier-2 fuzzy match can't bridge this: the closest
// real Theme member by JaroWinkler is `CardBackground`/`AccentBackground`,
// which is plausible but wrong; corpus cluster C0019 shows the agent
// invariably reaches for SolidBackground in the eventual fix. (Tuning report
// `docs/specs/tasks/038-tuning-reports/2026-05-11-525run.md` §"CS0117 — no
// static member on type".) Frequency bar is waived per Class B; structural
// justification is the citation to the XAML theme-resources doc in the vocab
// table.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class ThemeBackgroundSuffixRule : IRulePattern
{
    const string ThemeFqn = "Microsoft.UI.Reactor.Core.Theme";
    const string CanonicalTarget = "SolidBackground";
    const string BackgroundSuffix = "Background";

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
        // Same name we'd propose? Then the diagnostic must be on a different
        // axis (compiler reordering, multitree edge case). Silent rather than
        // emit a no-op suggestion.
        if (string.Equals(memberName, CanonicalTarget, StringComparison.Ordinal))
            return RuleSuggestion.Silent;

        var solid = ctx.Resolver.ResolveMember(themeType, CanonicalTarget);
        if (solid is null) return RuleSuggestion.Silent;

        return new RuleSuggestion(
            Text: $"Theme.{CanonicalTarget}",
            Confidence: 0.92,
            Evidence: $"Theme.{memberName} → Theme.{CanonicalTarget} (WinUI surface-background token)");
    }

    static string? ExtractMissingName(SyntaxNode node) => node switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax id => id.Identifier.ValueText,
        _ => null,
    };
}
