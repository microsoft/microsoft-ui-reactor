// Tier-3 rule contract. Spec 038 §6 + tasks doc §3.1 / §3.1a.
//
// A rule is a pure, syntax-tree-shaped pattern matcher: given a parsed
// diagnostic plus its semantic context, decide whether this exemplar matches
// the rule's documented (broken → fixed) shape and, if so, propose the
// rewrite as a single line of text. Rules MUST NOT touch the file system,
// the network, or spawn any process — and they MUST bind their target types
// and methods through RuleSymbolResolver (Roslyn ISymbol lookup against the
// live Compilation), never by raw string matching on syntax-tree text. The
// resolver gate (RuleTargetResolutionTests + the self-disabled state in
// --list-rules) is how we make API churn loud instead of silent.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.UI.Reactor.Cli.Check.Rules;

internal interface IRulePattern
{
    /// <summary>
    /// Stable, unique identifier — used by --disable-rule, --list-rules, and
    /// the telemetry payload. Must round-trip exactly through CLI parsing; the
    /// convention is PascalCase ending in "Rule" (e.g. "ThemeSolidBackgroundRule").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Provenance tag — "cluster:&lt;id&gt;" for Class A (induced from
    /// patterns.json) or "vocab:&lt;framework&gt;" for Class B
    /// (vocabulary-translation). Recorded with every fired suggestion so a
    /// future maintainer can trace the rule back to the data or doc that
    /// motivated it. Spec 038 §6.
    /// </summary>
    string Provenance { get; }

    /// <summary>
    /// Diagnostic codes this rule applies to (e.g. "CS1061", "CS1955"). The
    /// orchestrator skips TryMatch for any diagnostic whose code is not in
    /// this list — keeps the per-diagnostic cost flat as the rule count
    /// grows. Empty list = applies to every code (uncommon; rules usually
    /// anchor on one or two codes).
    /// </summary>
    IReadOnlyList<string> DiagnosticCodes { get; }

    /// <summary>
    /// Declares the fully-qualified type / method names this rule binds to,
    /// so the rule-target resolution CI gate (§3.1a) can fail the build when
    /// Reactor renames or removes a target the rule depends on. Each entry is
    /// a string the rule passes to <see cref="RuleSymbolResolver"/>. Rules
    /// with no declared targets (rare — pattern is purely syntactic, not
    /// symbol-anchored) return an empty array.
    /// </summary>
    IReadOnlyList<string> DeclaredTargets { get; }

    /// <summary>
    /// Inspects a single diagnostic. Returns a suggestion or
    /// <see cref="RuleSuggestion.Silent"/> when the rule looked at the
    /// diagnostic but didn't match. Implementations short-circuit to Silent
    /// whenever a declared target fails to resolve — the registry surfaces
    /// that as "self-disabled (unresolved: &lt;target&gt;)" in --list-rules.
    /// </summary>
    RuleSuggestion TryMatch(in RuleContext ctx);
}

/// <summary>
/// Inputs to <see cref="IRulePattern.TryMatch"/>. The semantic model and
/// compilation are required so rules can resolve target symbols via
/// <see cref="RuleSymbolResolver"/> instead of string-matching syntax text.
/// </summary>
internal readonly record struct RuleContext(
    SyntaxNode Node,
    Diagnostic Diagnostic,
    ITypeSymbol? Receiver,
    SemanticModel SemanticModel,
    CSharpCompilation Compilation,
    RuleSymbolResolver Resolver);

/// <summary>
/// A rule match. <see cref="Text"/> = null means "no match" (the silent
/// path). Confidence is on [0, 1]; rules SHOULD return ≥ 0.85 when they
/// match — Tier-3 fires on shape, not fuzz, so a low-confidence rule
/// result indicates the rule was the wrong tool. <see cref="Evidence"/>
/// is the short token (≤ 64 chars) appended after the rule's Name in the
/// stdout suffix; it captures the *concrete* reason the match fired.
/// </summary>
internal readonly record struct RuleSuggestion(string? Text, double Confidence, string Evidence)
{
    public static RuleSuggestion Silent { get; } = new(null, 0.0, "");

    public bool HasMatch => Text is not null;
}
