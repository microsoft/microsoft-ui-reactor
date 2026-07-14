using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_OPT_001: Detects an object-initializer / <c>with</c> member assignment
/// where the member is a selection sentinel (<c>SelectedIndex</c>,
/// <c>SelectedPageIndex</c>, <c>Date</c>) typed <c>Optional&lt;T&gt;</c>
/// and the right-hand side is the XAML-habit "nothing selected" literal
/// (<c>-1</c> / <c>null</c>).
/// </summary>
/// <remarks>
/// Since spec 050 the selection properties are <c>Optional&lt;T&gt;</c>, which has an
/// implicit <c>T -&gt; Optional&lt;T&gt;</c> conversion (<c>Optional.cs</c>). So
/// <c>element with { SelectedIndex = -1 }</c> becomes <c>Optional.Of(-1)</c> — a
/// force-assert "clear it" re-applied every render — rather than
/// <c>Optional&lt;T&gt;.Unset</c>, which lets the control own the selection. Both
/// compile; they mean opposite things at runtime. This is an <see
/// cref="DiagnosticSeverity.Info"/> nudge to make the intent explicit, not a
/// correctness error — <c>Optional.Of(-1)</c> is a documented-valid force-assert.
/// <c>SelectedItem</c>/<c>SelectedValue</c> are handled by a different rule (CTRL_001).
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OptionalSentinelAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_OPT_001";

    /// <summary>
    /// <see cref="Diagnostic.Properties"/> key carrying the fully-qualified
    /// (<c>global::</c>-prefixed) <c>Optional&lt;T&gt;</c> type to the code fix, so
    /// the fix can emit <c>Optional&lt;T&gt;.Unset</c> / <c>Optional&lt;T&gt;.Of(...)</c>
    /// that compiles even where the namespace is not imported.
    /// </summary>
    internal const string OptionalTypeProperty = "OptionalType";

    /// <summary>
    /// The <c>Microsoft.UI.Reactor</c> namespace that owns <c>Optional&lt;T&gt;</c>.
    /// </summary>
    private const string OptionalNamespace = "Microsoft.UI.Reactor";

    /// <summary>
    /// True selection sentinels only (spec 060 §4.2). Deliberately excludes value
    /// members like <c>Text</c>/<c>Password</c>/<c>IsChecked</c> — an empty string
    /// or <c>false</c> is a legitimate value, not a "nothing selected" sentinel.
    /// <c>SelectedItem</c>/<c>SelectedValue</c> are handled by CTRL_001.
    /// </summary>
    /// <remarks>
    /// <c>Time</c> is intentionally omitted although spec 060 §4.2 lists it: the only
    /// <c>Time</c> member (<c>TimePickerElement.Time</c>) is <c>Optional&lt;TimeSpan&gt;</c>
    /// — a non-nullable value type — so neither the <c>-1</c> nor the <c>null</c> sentinel
    /// is type-compatible and the entry could never fire on compilable code (it would only
    /// mislead). <c>Date</c> stays because <c>CalendarDatePickerElement.Date</c> is
    /// <c>Optional&lt;DateTimeOffset?&gt;</c> (nullable), so <c>Date = null</c> does compile
    /// into the silent <c>Optional.Of(null)</c> force-assert this rule targets.
    /// </remarks>
    internal static readonly ImmutableHashSet<string> SelectionMembers =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "SelectedIndex",
            "SelectedPageIndex",
            "Date");

    private static readonly LocalizableString Title =
        "Selection sentinel literal force-asserts instead of Optional<T>.Unset";

    private static readonly LocalizableString MessageFormat =
        "'{0}' is Optional<T>; the sentinel literal force-asserts the value every render. " +
        "Use Optional<T>.Unset to let the control own selection, or Optional<T>.Of(...) to keep the explicit force-assert.";

    private static readonly LocalizableString Description =
        "Since spec 050 the selection properties are Optional<T>, which has an implicit T -> Optional<T> " +
        "conversion. Assigning the XAML-habit sentinel (-1 / null) therefore becomes Optional.Of(sentinel) " +
        "— a force-assert re-applied every render — rather than Optional<T>.Unset, which lets the control own " +
        "the selection. Both compile; they mean opposite things at runtime. Make the intent explicit.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Controlled",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // Syntactic gate 1: a member assignment directly inside an object-initializer
        // (`new X { M = ... }`) or a with-initializer (`x with { M = ... }`). This
        // excludes ordinary statement assignments (`x.M = ...`).
        if (assignment.Parent is not InitializerExpressionSyntax initializer)
            return;
        if (!initializer.IsKind(SyntaxKind.ObjectInitializerExpression)
            && !initializer.IsKind(SyntaxKind.WithInitializerExpression))
            return;

        // Syntactic gate 2: the left-hand side is a bare member name in the
        // selection allowlist.
        if (assignment.Left is not IdentifierNameSyntax left)
            return;
        var member = left.Identifier.Text;
        if (!SelectionMembers.Contains(member))
            return;

        // Syntactic gate 3: the right-hand side is the sentinel literal (-1 or null).
        if (!IsSentinelLiteral(assignment.Right))
            return;

        // Semantic confirmation (one query): the member's declared type is Reactor's
        // Optional<T>. Non-Optional selection members (e.g. legacy `int SelectedIndex`)
        // pass the syntactic fast path but are rejected here.
        if (context.SemanticModel.GetTypeInfo(assignment.Left, context.CancellationToken).Type
                is not INamedTypeSymbol type
            || !IsReactorOptional(type))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(OptionalTypeProperty, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            assignment.GetLocation(),
            properties,
            member));
    }

    /// <summary>
    /// True when <paramref name="expression"/> is a selection sentinel: the integer
    /// literal <c>-1</c> or the <c>null</c> literal.
    /// </summary>
    internal static bool IsSentinelLiteral(ExpressionSyntax expression)
    {
        if (expression.IsKind(SyntaxKind.NullLiteralExpression))
            return true;

        return expression is PrefixUnaryExpressionSyntax unary
            && unary.IsKind(SyntaxKind.UnaryMinusExpression)
            && unary.Operand is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.NumericLiteralExpression)
            && literal.Token.ValueText == "1";
    }

    /// <summary>
    /// True when <paramref name="type"/> is <c>Microsoft.UI.Reactor.Optional&lt;T&gt;</c>.
    /// </summary>
    internal static bool IsReactorOptional(INamedTypeSymbol type) =>
        type.Name == "Optional"
        && type.TypeArguments.Length == 1
        && type.ContainingNamespace is { IsGlobalNamespace: false } ns
        && ns.ToDisplayString() == OptionalNamespace;
}
