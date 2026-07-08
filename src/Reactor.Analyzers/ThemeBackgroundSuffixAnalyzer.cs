using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_DYM_002 — an English-plausible but non-existent <c>Theme.*Background</c> token
/// (e.g. <c>Theme.AppBackground</c>, <c>Theme.LayerBackground</c>). The C# compiler already rejects
/// these with <c>CS0117</c> ("does not contain a definition"); this analyzer adds the actionable
/// <em>did-you-mean</em>: Reactor's canonical surface-background token is <c>Theme.SolidBackground</c>
/// (with <c>Theme.LayerBackground</c> → <c>Theme.LayerFill</c>). Paired with
/// <see cref="ThemeBackgroundSuffixCodeFix"/> for a one-click rename.
/// </summary>
/// <remarks>
/// <para>
/// This is a Phase-2 in-build "did you mean" analyzer (design of record: spec 061 §6). It mirrors
/// the deterministic <c>mur check</c> Tier-3 rule <c>ThemeBackgroundSuffixRule</c> (spec 038 §6): the
/// same <see cref="ExactOverrides"/> table, the same <see cref="SuffixFallbackTarget"/>, and the same
/// <c>Microsoft.UI.Reactor.Core.Theme</c> target. Behavioural parity with that rule is locked by a
/// cross-check test so the two cannot silently diverge.
/// </para>
/// <para>
/// <b>Precision.</b> It reports only when (a) the member access did not bind
/// (<see cref="SymbolInfo.Symbol"/> is <see langword="null"/>); (b) the receiver is
/// <em>exactly</em> <c>Microsoft.UI.Reactor.Core.Theme</c> — a look-alike <c>Theme</c> in another
/// namespace is ruled out by symbol equality; (c) the missing name ends in <c>Background</c>; and
/// (d) the resolved target still exists on the live <c>Theme</c> surface. Because it only fires when
/// the access failed to bind, it always co-occurs with CS0117 — so a
/// <see cref="DiagnosticSeverity.Warning"/> is safe under <c>TreatWarningsAsErrors</c> while still
/// surfacing in a plain <c>dotnet build</c>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ThemeBackgroundSuffixAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DYM_002";

    /// <summary>The Reactor type whose static tokens this analyzer nudges toward.</summary>
    public const string ThemeMetadataName = "Microsoft.UI.Reactor.Core.Theme";

    /// <summary>The canonical surface-background token every un-overridden <c>*Background</c> maps to.</summary>
    public const string SuffixFallbackTarget = "SolidBackground";

    private const string BackgroundSuffix = "Background";

    /// <summary>
    /// Exact-name overrides for <c>*Background</c> invented names that map to a real Theme member
    /// OTHER than <see cref="SuffixFallbackTarget"/>. Kept identical to the <c>mur check</c>
    /// <c>ThemeBackgroundSuffixRule</c> override table (asserted by a parity test).
    /// </summary>
    public static readonly ImmutableDictionary<string, string> ExactOverrides =
        ImmutableDictionary.CreateRange(StringComparer.Ordinal, new[]
        {
            new KeyValuePair<string, string>("LayerBackground", "LayerFill"),
        });

    /// <summary>
    /// Resolves the Theme token a <c>*Background</c> name should map to: an exact override if present,
    /// otherwise the surface-background fallback. Returns <see langword="null"/> for names that don't
    /// carry the <c>Background</c> suffix, or that already equal their own target (a no-op).
    /// </summary>
    public static string? ResolveTarget(string memberName)
    {
        if (!memberName.EndsWith(BackgroundSuffix, StringComparison.Ordinal))
            return null;
        var target = ExactOverrides.TryGetValue(memberName, out var overridden)
            ? overridden
            : SuffixFallbackTarget;
        return string.Equals(memberName, target, StringComparison.Ordinal) ? null : target;
    }

    private static readonly LocalizableString Title =
        "Reactor Theme has no such background token";
    private static readonly LocalizableString MessageFormat =
        "'Theme.{0}' does not exist — did you mean 'Theme.{1}'?";
    private static readonly LocalizableString Description =
        "An English-plausible Theme.*Background token was used that Reactor does not define. Reactor's surface-background token is Theme.SolidBackground (Theme.LayerBackground maps to Theme.LayerFill).";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.DidYouMean",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var theme = start.Compilation.GetTypeByMetadataName(ThemeMetadataName);
            if (theme is null)
                return; // Reactor's Theme surface isn't referenced — nothing to suggest.

            start.RegisterSyntaxNodeAction(
                ctx => AnalyzeMemberAccess(ctx, theme),
                SyntaxKind.SimpleMemberAccessExpression);
        });
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context, INamedTypeSymbol themeType)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        if (memberAccess.Name is not IdentifierNameSyntax name)
            return;

        var target = ResolveTarget(name.Identifier.Text);
        if (target is null)
            return;

        var model = context.SemanticModel;

        // Only when the static access itself did not bind (aligns with the compiler's CS0117).
        if (model.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not null)
            return;

        // The receiver must be exactly Reactor's Theme — not a user's look-alike Theme in another
        // namespace that happens to be missing a *Background member.
        if (model.GetSymbolInfo(memberAccess.Expression, context.CancellationToken).Symbol is not INamedTypeSymbol receiver)
            return;
        if (!SymbolEqualityComparer.Default.Equals(receiver, themeType))
            return;

        // The chosen target must still exist on the live Theme surface (self-disables through a rename).
        if (!themeType.GetMembers(target).Any())
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            name.GetLocation(),
            name.Identifier.Text,
            target));
    }
}
