// Class A (induced) rule. Spec 038 §6 + Validation Gate bar #2 cleared by
// `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md`.
//
// CS1955 on `Microsoft.UI.Reactor.GridSize` where the missing-member call
// shape is `GridSize.<NonInvocable>()`. The agent has typed parens after a
// static property — Reactor exposes `GridSize.Auto` as a property and
// `GridSize.Star(double = 1)` / `GridSize.Px(double)` as methods. Conditioned
// on the WinUI 3 mental model (`GridLength.Auto`, `RowDefinition.Height =
// new GridLength("Auto", GridUnitType.Auto)`) plus the visual symmetry with
// the Star/Px constructors, the agent reaches for `GridSize.Auto()`.
//
// Cross-agent reproducibility: 146 events combined (gpt-5.5 110, sonnet-4.6
// 36); both corpora are unanimously about `Auto` (every captured row has
// member="Auto"). Top-frequency cluster in both corpora — 10.7% / 9.8% of
// fixes respectively. Provenance is `cluster:CS1955-GridSize-other` rather
// than the per-run aggregator id (which is not stable across `mine
// aggregate` runs).
//
// First cross-tier rule: CS1955 is outside Tier-2's `SupportedCodes` (the
// orchestrator's `RulesCoverCode` path routes it through). The rule's
// existence flips the diag from "no suggestion" to a high-confidence
// shape-anchored fix.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class GridSizeFactoryParensRule : IRulePattern
{
    const string GridSizeFqn = "Microsoft.UI.Reactor.GridSize";

    public string Name => "GridSizeFactoryParensRule";
    public string Provenance => "cluster:CS1955-GridSize-other";
    public IReadOnlyList<string> DiagnosticCodes { get; } = new[] { "CS1955" };
    public IReadOnlyList<string> DeclaredTargets { get; } = new[] { GridSizeFqn };

    public RuleSuggestion TryMatch(in RuleContext ctx)
    {
        if (ctx.Diagnostic.Id != "CS1955") return RuleSuggestion.Silent;

        var gridSize = ctx.Resolver.ResolveType(GridSizeFqn);
        if (gridSize is null) return RuleSuggestion.Silent;

        // CS1955's syntax shape is `Receiver.Member()` — the node we want is
        // the inner MemberAccessExpression. PickRelevantNode typically lands
        // on the MemberAccess directly; the InvocationExpression and bare
        // IdentifierName cases are belt-and-suspenders for diag-location
        // variance across MSBuild emitters.
        var memberAccess = ExtractMemberAccess(ctx.Node);
        if (memberAccess is null) return RuleSuggestion.Silent;

        // Receiver must resolve to Microsoft.UI.Reactor.GridSize specifically.
        // A user's `Acme.GridSize` with a coincidentally-named static property
        // must NOT trigger this rule — symbol equality against the resolved
        // Reactor type is the load-bearing namespace gate.
        var receiverType = ctx.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (receiverType is null) return RuleSuggestion.Silent;
        if (!SymbolEqualityComparer.Default.Equals(receiverType, gridSize))
            return RuleSuggestion.Silent;

        var memberName = memberAccess.Name.Identifier.ValueText;

        // Confirm the named member exists on GridSize and is NOT a method.
        // RuleSymbolResolver.ResolveMember filters method symbols out, so a
        // null return here covers both "no such member" (which would have
        // emitted CS0117 anyway, not CS1955) and "the name is a method"
        // (which wouldn't have emitted CS1955 either, but stays defensive).
        var member = ctx.Resolver.ResolveMember(gridSize, memberName);
        if (member is null) return RuleSuggestion.Silent;

        var kind = member.Kind switch
        {
            SymbolKind.Property => "property",
            SymbolKind.Field => "field",
            _ => "member",
        };

        return new RuleSuggestion(
            Text: $"GridSize.{memberName}",
            Confidence: 0.95,
            Evidence: $"GridSize.{memberName} is a static {kind} — drop the parens");
    }

    static MemberAccessExpressionSyntax? ExtractMemberAccess(SyntaxNode node) => node switch
    {
        MemberAccessExpressionSyntax m => m,
        InvocationExpressionSyntax inv when inv.Expression is MemberAccessExpressionSyntax m => m,
        IdentifierNameSyntax id when id.Parent is MemberAccessExpressionSyntax mp && mp.Name == id => mp,
        _ => null,
    };
}
