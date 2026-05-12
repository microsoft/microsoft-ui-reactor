// Class A (induced) rule. Spec 038 §6 + Validation Gate bar #2 cleared by
// `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md`.
//
// CS0117 on `Microsoft.UI.Reactor.GridSize` where the missing-member name is
// one of {Pixel, Pixels, Fixed}. The agent has typed the WPF/WinUI legacy
// name — `GridLength.Pixel`, `GridUnitType.Pixel`, `RowDefinition.Height =
// new GridLength(pixels, GridUnitType.Pixel)`, or the WPF "Fixed" XAML
// keyword — for what Reactor exposes as the abbreviation `GridSize.Px(...)`.
//
// Cross-agent reproducibility: 9 events combined (gpt-5.5 5 / sonnet 4),
// 100% rewrite target on every captured row is `GridSize.Px(<same numeric
// arg>)`. The fix is purely a method-name rename; the argument list and
// surrounding column/row literal are unchanged. Provenance
// `cluster:CS0117-GridSize-renamed_member` per the audit's cluster-key
// scheme.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class GridSizePxRenameRule : IRulePattern
{
    const string GridSizeFqn = "Microsoft.UI.Reactor.GridSize";
    const string CorrectMember = "Px";

    // WPF / WinUI / legacy-XAML names for the pixel-track constructor that
    // Reactor renamed to `Px`. All three appear in the 525-run corpora at
    // count ≥ 1 across both agents; case-sensitive match is intentional
    // since C# identifiers are case-sensitive and the WPF/WinUI APIs are
    // PascalCase exactly as listed.
    static readonly string[] LegacyNames = { "Pixel", "Pixels", "Fixed" };

    public string Name => "GridSizePxRenameRule";
    public string Provenance => "cluster:CS0117-GridSize-renamed_member";
    public IReadOnlyList<string> DiagnosticCodes { get; } = new[] { "CS0117" };
    public IReadOnlyList<string> DeclaredTargets { get; } = new[] { GridSizeFqn };

    public RuleSuggestion TryMatch(in RuleContext ctx)
    {
        if (ctx.Diagnostic.Id != "CS0117") return RuleSuggestion.Silent;

        var gridSize = ctx.Resolver.ResolveType(GridSizeFqn);
        if (gridSize is null) return RuleSuggestion.Silent;

        var memberAccess = ExtractMemberAccess(ctx.Node);
        if (memberAccess is null) return RuleSuggestion.Silent;

        // Receiver namespace gate: a user's `Acme.GridSize` with a member
        // called `Pixel` must NOT trigger this rule. Symbol equality against
        // the resolved Reactor type is the load-bearing safety check.
        var receiverType = ctx.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType is null) return RuleSuggestion.Silent;
        if (!SymbolEqualityComparer.Default.Equals(receiverType, gridSize))
            return RuleSuggestion.Silent;

        var memberName = memberAccess.Name.Identifier.ValueText;
        if (Array.IndexOf(LegacyNames, memberName) < 0) return RuleSuggestion.Silent;

        // Post-gate: confirm `Px` still exists on GridSize. If a future
        // Reactor release renames `Px` (or removes it), the rule self-skips
        // here rather than emitting a suggestion the build won't accept.
        // DeclaredTargets only proves the containing type exists.
        var pxMethod = ctx.Resolver.ResolveMethod(gridSize, CorrectMember);
        if (pxMethod is null) return RuleSuggestion.Silent;

        return new RuleSuggestion(
            Text: $"GridSize.{CorrectMember}(...)",
            Confidence: 0.95,
            Evidence: $"GridSize.{memberName}(...) → GridSize.{CorrectMember}(...) — same numeric arg; `{memberName}` is the WPF/WinUI legacy name, Reactor uses `{CorrectMember}`");
    }

    static MemberAccessExpressionSyntax? ExtractMemberAccess(SyntaxNode node) => node switch
    {
        MemberAccessExpressionSyntax m => m,
        InvocationExpressionSyntax inv when inv.Expression is MemberAccessExpressionSyntax m => m,
        IdentifierNameSyntax id when id.Parent is MemberAccessExpressionSyntax mp && mp.Name == id => mp,
        _ => null,
    };
}
