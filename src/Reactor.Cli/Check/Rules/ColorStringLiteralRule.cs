// Mirror of the REACTOR_DYM_006 analyzer (ColorStringLiteralAnalyzer) — issue #1375.
//
// A compile-time-constant color string that BrushHelper can't parse compiles
// fine and silently renders gray, so unlike every other Tier-3 rule there is
// no compiler error to hang this on. The rule therefore anchors on the
// analyzer's own diagnostic ID: `dotnet build` (which `mur check` wraps) runs
// the shipped Reactor.Analyzers bundle and REACTOR_DYM_006 lands in the parsed
// output.
//
// The analyzer has already done the work: every fixable REACTOR_DYM_006
// message ends in `did you mean X?`, computed for the exact literal the
// diagnostic points at. The rule lifts X out of that message rather than
// re-deriving it, so it can never disagree with the IDE code fix — and since
// CheckCommand truncates messages to 100 chars, the `→ try:` suffix is what
// keeps the rewrite visible. A *FontFamily key without the FontFamily overload
// has no rewrite of the flagged argument, but its message names the
// `.FontFamily("…")` element modifier as the way to set the font; the rule
// surfaces that too, because truncation would otherwise hide it. Messages with
// no concrete next step (unknown name, malformed hex) stay silent.
// It deliberately has NO Tier-1 HintFor entry in CheckCommand — that table
// wins ties and would mask the suggestion.
//
// DidYouMeanAnalyzerParityTests feeds real analyzer diagnostics through this
// rule and asserts it suggests the code fix's rewrite.

using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal sealed partial class ColorStringLiteralRule : IRulePattern
{
    internal const string DiagnosticId = "REACTOR_DYM_006";
    const string BrushHelperFqn = "Microsoft.UI.Reactor.BrushHelper";
    const string ResourceBuilderFqn = "Microsoft.UI.Reactor.Elements.ResourceBuilder";
    const string ElementExtensionsFqn = "Microsoft.UI.Reactor.ElementExtensions";

    public string Name => "ColorStringLiteralRule";
    public string Provenance => "analyzer:REACTOR_DYM_006";
    public IReadOnlyList<string> DiagnosticCodes { get; } = new[] { DiagnosticId };
    public IReadOnlyList<string> DeclaredTargets { get; } = new[] { BrushHelperFqn, ResourceBuilderFqn, ElementExtensionsFqn };

    // `'<value>' is not a color string Reactor can parse, so it renders gray — <detail>; did you mean <X>?`
    [GeneratedRegex(@"^'(?<value>.*?)' is not a color string Reactor can parse\b.*; did you mean (?<suggestion>.+)\?$", RegexOptions.Singleline)]
    private static partial Regex MessagePattern();

    // `… — '<key>' is a FontFamily resource, but this Set overload only stores color brushes; to change the font, use <X> on the element`
    [GeneratedRegex(@"^'.*?' is not a color string Reactor can parse\b.*; to change the font, use (?<modifier>\.FontFamily\(.+\)) on the element$", RegexOptions.Singleline)]
    private static partial Regex FontModifierPattern();

    public RuleSuggestion TryMatch(in RuleContext ctx)
    {
        if (ctx.Diagnostic.Id != DiagnosticId) return RuleSuggestion.Silent;

        var message = ctx.Diagnostic.GetMessage();
        var match = MessagePattern().Match(message);
        if (!match.Success)
        {
            var modifier = FontModifierPattern().Match(message);
            return modifier.Success
                ? new RuleSuggestion(
                    Text: modifier.Groups["modifier"].Value,
                    Confidence: 0.9,
                    Evidence: "*FontFamily key: Set(key, string) only stores brushes")
                : RuleSuggestion.Silent;
        }

        var suggestion = match.Groups["suggestion"].Value;
        var value = match.Groups["value"].Value;
        var evidence = suggestion.Contains(".FontFamily(", StringComparison.Ordinal)
            ? "*FontFamily key → FontFamily overload"
            : $"'{Truncate(value)}' renders gray";

        return new RuleSuggestion(Text: suggestion, Confidence: 0.95, Evidence: evidence);
    }

    static string Truncate(string s) => s.Length <= 24 ? s : s[..21] + "...";
}
