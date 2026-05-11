// Class B vocabulary-translation rule. Spec 038 §6.
//
// CS1061 on `Microsoft.UI.Reactor.Core.ButtonElement` where the missing member
// is `OnClick`. The agent has carried over the WPF / WinUI mental model where
// `Button.Click += handler` (or a `.OnClick(...)` fluent chain) is the
// idiomatic event hookup. Reactor inverts the convention: `onClick` is a
// factory parameter on `Button(...)`, not a chainable method on the resulting
// ButtonElement. The correct move is to push the lambda INTO the factory call
// as a named argument — NOT to reach for `.OnTapped(...)` (the gesture-level
// event, which fires on different input semantics and is not the intended
// translation).
//
// Tier-2 already covers this case via the "named-argument move" path in
// `SymbolSuggester.SuggestForCS1061` (factory-index probe at conf 0.90 — see
// the canonical Button("x").OnClick comment there). This rule wins per spec
// §6 "rule wins over Tier-2 fuzzy match" because:
//   1. The shape-anchored rule's evidence string spells out the
//      .OnClick → factory-arg translation explicitly AND names the wrong
//      alternative (.OnTapped) the agent might otherwise reach for.
//   2. The receiver-anchored binding via RuleSymbolResolver protects the
//      rule against a future Reactor minor renaming ButtonElement (the
//      §3.1a CI gate will fail the build, not silently mis-fire).
//
// Provenance is `vocab:WinUI3` rather than `cluster:<id>` — this case is
// documented in SKILL.md as the canonical Reactor authoring convention; the
// 525-run corpus contains it only via cousin patterns (cluster C0102 covers
// the same shape on `BorderElement`, count=1, well below the Class-A
// frequency floor). The justification is structural (WinUI 3 Button.Click
// event docs cited in `docs/specs/tasks/038-vocab-table.csv`), not corpus-
// empirical, which is exactly the Class-B carve-out per spec §6.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class ButtonOnClickFactoryMoveRule : IRulePattern
{
    const string ButtonElementFqn = "Microsoft.UI.Reactor.Core.ButtonElement";
    const string FactoriesFqn = "Microsoft.UI.Reactor.Factories";
    const string MissingMember = "OnClick";
    const string FactoryParameter = "onClick";

    public string Name => "ButtonOnClickFactoryMoveRule";
    public string Provenance => "vocab:WinUI3";
    public IReadOnlyList<string> DiagnosticCodes { get; } = new[] { "CS1061" };
    public IReadOnlyList<string> DeclaredTargets { get; } = new[]
    {
        ButtonElementFqn,
        FactoriesFqn,
    };

    public RuleSuggestion TryMatch(in RuleContext ctx)
    {
        if (ctx.Diagnostic.Id != "CS1061") return RuleSuggestion.Silent;
        if (ctx.Receiver is null) return RuleSuggestion.Silent;

        var button = ctx.Resolver.ResolveType(ButtonElementFqn);
        if (button is null) return RuleSuggestion.Silent;

        // Receiver must be ButtonElement exactly. A subclass / look-alike from
        // another namespace should fall through to Tier-2 (or no suggestion).
        if (!SymbolEqualityComparer.Default.Equals(ctx.Receiver, button))
            return RuleSuggestion.Silent;

        var memberName = ExtractMissingName(ctx.Node);
        if (!string.Equals(memberName, MissingMember, StringComparison.Ordinal))
            return RuleSuggestion.Silent;

        // Confirm the Button factory still has an `onClick` parameter on at
        // least one overload. Cheap post-gate check — if Reactor renamed
        // the parameter, the rule self-skips here rather than emitting a
        // suggestion the build won't accept. (DeclaredTargets only proves
        // the containing type exists; parameter-shape requires this probe.)
        var factories = ctx.Resolver.ResolveType(FactoriesFqn);
        if (factories is null) return RuleSuggestion.Silent;
        if (!FactoryAcceptsOnClick(factories)) return RuleSuggestion.Silent;

        return new RuleSuggestion(
            Text: "Button(..., onClick: ...)",
            Confidence: 0.95,
            Evidence: "ButtonElement does not chain .OnClick — move the lambda into the Button(...) factory call as the onClick named arg (not .OnTapped, which is a gesture event)");
    }

    static bool FactoryAcceptsOnClick(INamedTypeSymbol factories)
    {
        foreach (var member in factories.GetMembers("Button"))
        {
            if (member is not IMethodSymbol method) continue;
            foreach (var p in method.Parameters)
            {
                if (string.Equals(p.Name, FactoryParameter, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    static string? ExtractMissingName(SyntaxNode node) => node switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax id => id.Identifier.ValueText,
        _ => null,
    };
}
