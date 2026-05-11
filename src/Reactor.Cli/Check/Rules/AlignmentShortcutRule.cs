// Class B vocabulary-translation rule. Spec 038 §6, vocab table rows
// "WinUI3 HorizontalAlignment → HAlign" and "WinUI3 VerticalAlignment → VAlign".
//
// CS1061 on a Reactor-namespaced receiver where the missing member is
// `HorizontalAlignment` or `VerticalAlignment`. The agent has carried over
// the WinUI 3 FrameworkElement property name; Reactor exposes the fluent
// shortcuts `.HAlign(HorizontalAlignment)` and `.VAlign(VerticalAlignment)`
// from `Microsoft.UI.Reactor.ElementExtensions`.
//
// Why Tier-2 fuzzy-match misses this: JaroWinkler("HorizontalAlignment", "HAlign")
// is ≈ 0.55, well below the 0.70 similarity floor. Tier-2 falls through to the
// next-closest member and emits the wrong sibling (see 525-run report
// "CS1061 — member missing" subsection). Cluster C0017 + adjacent ≈ 22 events.
// Frequency bar is waived per Class B; structural justification is the
// citation to the WinUI 3 FrameworkElement docs in the vocab table.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.UI.Reactor.Cli.Check.Suggesters;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed class AlignmentShortcutRule : IRulePattern
{
    const string ExtensionsFqn = "Microsoft.UI.Reactor.ElementExtensions";

    public string Name => "AlignmentShortcutRule";
    public string Provenance => "vocab:WinUI3";
    public IReadOnlyList<string> DiagnosticCodes { get; } = new[] { "CS1061" };
    public IReadOnlyList<string> DeclaredTargets { get; } = new[] { ExtensionsFqn };

    public RuleSuggestion TryMatch(in RuleContext ctx)
    {
        if (ctx.Diagnostic.Id != "CS1061") return RuleSuggestion.Silent;
        if (ctx.Receiver is null) return RuleSuggestion.Silent;

        // Strong receiver filter: only fire on Reactor-namespaced types. A
        // user's own POCO that happens to be missing a `HorizontalAlignment`
        // member should NOT get a Reactor-vocab nudge.
        if (!SymbolSuggester.IsReactorType(ctx.Receiver)) return RuleSuggestion.Silent;

        var extensions = ctx.Resolver.ResolveType(ExtensionsFqn);
        if (extensions is null) return RuleSuggestion.Silent;

        var memberName = ExtractMissingName(ctx.Node);
        if (memberName is null) return RuleSuggestion.Silent;

        var (shortcut, valueType) = memberName switch
        {
            "HorizontalAlignment" => ("HAlign", "HorizontalAlignment"),
            "VerticalAlignment" => ("VAlign", "VerticalAlignment"),
            _ => (null, null),
        };
        if (shortcut is null) return RuleSuggestion.Silent;

        // Confirm the shortcut extension still lives on ElementExtensions in
        // the current Reactor surface. A future rename → self-skip via the
        // §3.1a CI gate path (ResolveMethod returning null is the post-gate
        // check; DeclaredTargets only proves the containing type exists).
        if (ctx.Resolver.ResolveMethod(extensions, shortcut!) is null)
            return RuleSuggestion.Silent;

        return new RuleSuggestion(
            Text: $".{shortcut}({valueType}.<value>)",
            Confidence: 0.92,
            Evidence: $".{memberName}(...) → .{shortcut}(...) (Reactor fluent shortcut)");
    }

    static string? ExtractMissingName(SyntaxNode node) => node switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax id => id.Identifier.ValueText,
        _ => null,
    };
}
