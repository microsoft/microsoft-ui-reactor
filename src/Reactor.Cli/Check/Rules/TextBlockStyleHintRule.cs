// Class A (induced) rule. Spec 038 §6 + Validation Gate bar #2 cleared by
// `docs/specs/tasks/038-tuning-reports/2026-05-11-cross-agent-audit.md`
// (the cross-agent bar collapses two corpus-distinct syntactic shapes that
// translate to the same fix concept; see below).
//
// CS1061 or CS0117 on `Microsoft.UI.Reactor.Core.TextBlockElement` where the
// missing-member name is exactly `Style`. The agent has carried over the
// WPF / WinUI mental model where `TextBlock.Style = MyTitleStyle` is the
// idiomatic way to attach typography via a `Style` resource. Reactor does
// not expose a `Style` property/member — typography is configured through
// the fluent helpers `.FontSize(...) / .Bold() / .SemiBold() / .Italic() /
// .Foreground(...)` directly on the TextBlock element.
//
// Two syntactic shapes appear in the 525-run corpora, partitioned by agent:
//   - **gpt-5.5** (2 events) — fluent `.Style(...)` invocation. CS1061
//     on `TextBlock("...").Style(Theme.TitleTextBlockStyle)`.
//   - **claude-sonnet-4.6** (3 events) — record `with`-expression. CS0117
//     on `(TextBlock("...") with { Style = TitleStyle })`.
//
// Cross-agent reproducibility: collapsed across the two shapes (5 events
// combined, both agents represented) the conceptual fix is identical —
// drop `Style` for the fluent helpers. Treating the two as one cluster
// is the audit's "STRONG after fix_kind classifier collapse" verdict.
// Provenance `cluster:TextBlockElement-Style-rewrite`.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class TextBlockStyleHintRule : IRulePattern
{
    const string TextBlockElementFqn = "Microsoft.UI.Reactor.Core.TextBlockElement";
    const string MissingMember = "Style";

    public string Name => "TextBlockStyleHintRule";
    public string Provenance => "cluster:TextBlockElement-Style-rewrite";
    public IReadOnlyList<string> DiagnosticCodes { get; } = new[] { "CS1061", "CS0117" };
    public IReadOnlyList<string> DeclaredTargets { get; } = new[] { TextBlockElementFqn };

    public RuleSuggestion TryMatch(in RuleContext ctx)
    {
        if (ctx.Diagnostic.Id is not ("CS1061" or "CS0117")) return RuleSuggestion.Silent;

        var textBlock = ctx.Resolver.ResolveType(TextBlockElementFqn);
        if (textBlock is null) return RuleSuggestion.Silent;

        var (receiverType, memberName) = ExtractReceiverAndMember(ctx);
        if (memberName != MissingMember) return RuleSuggestion.Silent;
        if (receiverType is null) return RuleSuggestion.Silent;
        if (!SymbolEqualityComparer.Default.Equals(receiverType, textBlock))
            return RuleSuggestion.Silent;

        return new RuleSuggestion(
            Text: ".FontSize(...) / .SemiBold() / .Bold() / .Italic() / .Foreground(...)",
            Confidence: 0.88,
            Evidence: "TextBlockElement does not expose .Style — Reactor configures typography through fluent helpers directly on the element");
    }

    /// <summary>
    /// Resolves the (receiver-type, missing-member-name) pair across the two
    /// captured syntactic shapes. Returns (null, null) when the diagnostic
    /// doesn't match either shape.
    /// </summary>
    static (ITypeSymbol? receiver, string? member) ExtractReceiverAndMember(in RuleContext ctx)
    {
        // Shape 1 — fluent `.Style(...)` invocation: CS1061 on
        // `instance.Style(...)`. PickRelevantNode lands on the
        // MemberAccessExpression directly.
        if (ctx.Node is MemberAccessExpressionSyntax memberAccess)
        {
            var receiver = ctx.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
            return (receiver, memberAccess.Name.Identifier.ValueText);
        }

        // Variance: PickRelevantNode landed on the Name part of a MemberAccess.
        if (ctx.Node is IdentifierNameSyntax id1 &&
            id1.Parent is MemberAccessExpressionSyntax mp && mp.Name == id1)
        {
            var receiver = ctx.SemanticModel.GetTypeInfo(mp.Expression).Type;
            return (receiver, id1.Identifier.ValueText);
        }

        // Shape 2 — record `with`-expression: CS0117 on
        // `expr with { Style = ... }`. The diag points at the `Style`
        // identifier inside the InitializerExpression; receiver type comes
        // from the WithExpression's left-hand expression.
        if (ctx.Node is IdentifierNameSyntax id2 &&
            id2.Parent is AssignmentExpressionSyntax assign &&
            assign.Left == id2 &&
            assign.Parent is InitializerExpressionSyntax init &&
            init.Parent is WithExpressionSyntax withExpr)
        {
            var receiver = ctx.SemanticModel.GetTypeInfo(withExpr.Expression).Type;
            return (receiver, id2.Identifier.ValueText);
        }

        return (null, null);
    }
}
