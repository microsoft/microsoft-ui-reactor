using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_HOOKS_011: Detects a controlled-input DSL factory call
/// (<c>TextBox</c>, <c>PasswordBox</c>, <c>NumberBox</c>, <c>Slider</c>,
/// <c>ComboBox</c>, <c>CheckBox</c>, <c>ToggleSwitch</c>, <c>RatingControl</c>,
/// <c>CalendarDatePicker</c>, …) where the value argument is <b>state-derived</b>
/// (an identifier / member access) but the change callback is <b>present yet inert</b>
/// (an empty-block lambda, or a lambda that never references its parameter). The
/// control renders the value but silently drops the user's edits — the XAML
/// <c>Mode=OneWay</c> / "read it back via <c>x:Name</c>" habit.
/// </summary>
/// <remarks>
/// <para>
/// Gating (spec 060 §4.1): the rule fires only when an explicit state-derived value
/// argument is supplied. A bare <c>TextBox("label")</c> — or any call that simply
/// <i>omits</i> the callback — is a legitimate read-only display and is left alone.
/// </para>
/// <para>
/// The factory shapes are not uniform (ComboBox's value is its 2nd argument; Slider's
/// callback is its 4th), so the value and callback arguments are located by matching
/// each factory parameter's type — the single <c>Optional&lt;T&gt;</c> value parameter
/// and the first delegate (callback) parameter — rather than by fixed position. This
/// makes the rule robust to positional and named arguments alike.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControlledInputAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_HOOKS_011";

    /// <summary>
    /// Property key handed to the code fix carrying the read-only modifier name.
    /// Only set for controls that actually expose an <c>IsReadOnly</c> modifier
    /// (TextBox, RatingControl); absent for nudge-only controls.
    /// </summary>
    internal const string ReadOnlyModifierProperty = "ReadOnlyModifier";

    /// <summary>
    /// Controlled-input factory names scanned by the syntactic fast path. Every
    /// entry is a <c>Microsoft.UI.Reactor.Factories</c> method with exactly one
    /// <c>Optional&lt;T&gt;</c> "value" parameter and at least one delegate
    /// change-callback parameter (verified against <c>Dsl.cs</c>).
    /// </summary>
    /// <remarks>
    /// The collection controls that also expose a parallel <c>int?</c>
    /// <c>selectedIndex</c> overload (TabView, Pivot, ListView, GridView, FlipView —
    /// <c>Dsl.cs:900-1060</c>) are intentionally omitted: a plain <c>int</c> call is
    /// ambiguous between the <c>int?</c> and <c>Optional&lt;int&gt;</c> overloads, so
    /// the rule's "single <c>Optional&lt;T&gt;</c> value parameter" model can't bind
    /// reliably. The listed selection controls (ComboBox, RadioButtons, ListBox,
    /// SelectorBar, PipsPager) expose <b>only</b> the <c>Optional&lt;int&gt;</c>
    /// overload and bind cleanly.
    /// </remarks>
    private static readonly ImmutableHashSet<string> ControlledInputFactories =
        ImmutableHashSet.Create(
            System.StringComparer.Ordinal,
            "TextBox", "PasswordBox", "NumberBox", "RichEditBox", "AutoSuggestBox",
            "Slider", "RatingControl",
            "CheckBox", "ThreeStateCheckBox", "RadioButton", "ToggleSwitch",
            "ComboBox", "RadioButtons", "ListBox", "SelectorBar", "PipsPager",
            "ColorPicker", "CalendarDatePicker", "DatePicker", "TimePicker");

    /// <summary>
    /// Factory name → the read-only modifier that makes the intent explicit. Only
    /// TextBox and RatingControl expose one (<c>ElementExtensions.cs:876,1420</c>);
    /// every other control is nudge-only. We deliberately never suggest
    /// <c>.IsEnabled(false)</c> — that disables and de-focuses the control, a
    /// destructive behavior different from a read-only display.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ReadOnlyModifierByFactory =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "TextBox",       "IsReadOnly" },
            { "RatingControl", "IsReadOnly" },
        };

    private static readonly LocalizableString Title =
        "Controlled input has a state-derived value but an inert change callback";

    private static readonly LocalizableString MessageFormat =
        "'{0}' is bound to a state-derived value but its change callback is empty or " +
        "ignores the changed value; the control renders the value and silently drops " +
        "user edits (fake 'Mode=OneWay'). Wire the callback to update state, or make " +
        "the read-only intent explicit.";

    private static readonly LocalizableString Description =
        "A controlled input renders the value you pass and reports the user's edits " +
        "through its change callback. When the value is state-derived but the callback " +
        "is an empty lambda (or never reads its parameter), the control snaps back to " +
        "the old value on the next render and the edit is lost — the XAML 'I'll read it " +
        "back via x:Name' habit. Feed the new value into state from the callback, or, " +
        "for a genuinely read-only display, make that explicit (e.g. .IsReadOnly(true)).";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Hooks",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Syntactic fast path — cheap gate before any semantic work.
        var invokedName = GetInvokedSimpleName(invocation.Expression);
        if (invokedName is null || !ControlledInputFactories.Contains(invokedName))
            return;

        // Semantic confirm: the callee must resolve to a Reactor DSL factory.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method)
            return;
        if (!IsReactorFactory(method))
            return;

        // An explicit .IsReadOnly(true) already downstream in the fluent chain is the
        // author declaring intentional read-only display — the code fix's own output,
        // and a legitimate hand-written form. Do not fire (and let the fix converge).
        if (IsMarkedReadOnly(invocation))
            return;

        // Locate the value (Optional<T>) and change-callback (delegate) parameters by
        // type — the factory shapes are not positionally uniform.
        var valueParam = method.Parameters.FirstOrDefault(p => IsOptionalT(p.Type));
        var callbackParam = method.Parameters.FirstOrDefault(p => p.Type.TypeKind == TypeKind.Delegate);
        if (valueParam is null || callbackParam is null)
            return;

        var valueArg = FindArgumentFor(invocation, valueParam);
        var callbackArg = FindArgumentFor(invocation, callbackParam);

        // Gate (a): the value argument must be present AND state-derived. This keeps
        // the rule to the "I wired a value but forgot the setter" case — a bare
        // read-only display (literal value, or value omitted) is left alone.
        if (valueArg is null || !IsStateDerived(valueArg.Expression))
            return;

        // Gate (b): the callback must be PRESENT (omitting it is fine) and inert —
        // an empty-block lambda or a lambda that never references its parameter.
        if (callbackArg is null || !CallbackDropsValue(callbackArg.Expression))
            return;

        var properties = ImmutableDictionary<string, string?>.Empty;
        if (ReadOnlyModifierByFactory.TryGetValue(invokedName, out var modifier))
            properties = properties.Add(ReadOnlyModifierProperty, modifier);

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            properties,
            invokedName));
    }

    /// <summary>The simple name being invoked, whether via <c>using static</c>
    /// (<c>TextBox(...)</c>) or qualified (<c>Factories.TextBox(...)</c>).</summary>
    private static string? GetInvokedSimpleName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
        _ => null,
    };

    private static bool IsReactorFactory(IMethodSymbol method) =>
        method.ContainingType is { Name: "Factories" } containing
        && containing.ContainingNamespace?.ToDisplayString() == "Microsoft.UI.Reactor";

    /// <summary>
    /// True when this factory call is followed by an explicit <c>.IsReadOnly(true)</c>
    /// (or the no-argument <c>.IsReadOnly()</c>, which defaults to <c>true</c>) anywhere
    /// in its fluent chain — the author's declaration of intentional read-only display.
    /// Intervening modifiers (e.g. <c>.Margin(8)</c>) are walked through.
    /// </summary>
    private static bool IsMarkedReadOnly(InvocationExpressionSyntax invocation) =>
        FindIsReadOnlyInChain(invocation, requireTrue: true);

    /// <summary>
    /// True when this factory call's fluent chain contains <b>any</b> <c>.IsReadOnly(...)</c>
    /// call (regardless of argument). Used by the code fix to avoid wrapping a call that
    /// already sets read-only-ness — appending <c>.IsReadOnly(true)</c> after an explicit
    /// <c>.IsReadOnly(false)</c> would produce contradictory modifiers whose last-writer
    /// value is still <c>false</c>.
    /// </summary>
    internal static bool HasIsReadOnlyModifier(InvocationExpressionSyntax invocation) =>
        FindIsReadOnlyInChain(invocation, requireTrue: false);

    private static bool FindIsReadOnlyInChain(InvocationExpressionSyntax invocation, bool requireTrue)
    {
        SyntaxNode current = invocation;
        while (current.Parent is MemberAccessExpressionSyntax memberAccess
               && memberAccess.Expression == current
               && memberAccess.Parent is InvocationExpressionSyntax outerInvocation)
        {
            if (memberAccess.Name.Identifier.ValueText == "IsReadOnly"
                && (!requireTrue || IsReadOnlyTrue(outerInvocation.ArgumentList)))
                return true;
            current = outerInvocation;
        }
        return false;
    }

    private static bool IsReadOnlyTrue(ArgumentListSyntax argumentList)
    {
        var args = argumentList.Arguments;
        if (args.Count == 0)
            return true; // .IsReadOnly() defaults to true
        if (args.Count == 1)
            return Unwrap(args[0].Expression).IsKind(SyntaxKind.TrueLiteralExpression);
        return false;
    }

    private static bool IsOptionalT(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Optional", Arity: 1 } named
        && named.ContainingNamespace?.ToDisplayString() == "Microsoft.UI.Reactor";

    /// <summary>
    /// Resolve the argument bound to <paramref name="parameter"/>, honoring both
    /// named arguments and positional slots. Returns <c>null</c> when the parameter
    /// was omitted (left to its default).
    /// </summary>
    private static ArgumentSyntax? FindArgumentFor(InvocationExpressionSyntax invocation, IParameterSymbol parameter)
    {
        var args = invocation.ArgumentList.Arguments;

        // Named argument wins wherever it appears.
        foreach (var arg in args)
        {
            if (arg.NameColon?.Name.Identifier.ValueText == parameter.Name)
                return arg;
        }

        // Positional: the argument at the parameter's ordinal, but only if that slot
        // is actually positional (not a named argument targeting another parameter).
        if (parameter.Ordinal < args.Count)
        {
            var candidate = args[parameter.Ordinal];
            if (candidate.NameColon is null)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// True when the value expression reads live state — an identifier or a member
    /// access — as opposed to a literal, a <c>default</c>, or the explicit
    /// <c>Optional&lt;T&gt;.Unset</c> "uncontrolled" sentinel.
    /// </summary>
    private static bool IsStateDerived(ExpressionSyntax expression)
    {
        expression = Unwrap(expression);
        return expression switch
        {
            IdentifierNameSyntax => true,
            MemberAccessExpressionSyntax ma => !IsOptionalUnsetSentinel(ma),
            _ => false,
        };
    }

    /// <summary>
    /// True only for the <c>Optional&lt;T&gt;.Unset</c> (or <c>Optional.Unset</c>)
    /// sentinel — a member access named <c>Unset</c> whose receiver's simple name is
    /// <c>Optional</c>. A live member access like <c>model.Unset</c> or <c>Props.Unset</c>
    /// is NOT the sentinel and stays state-derived.
    /// </summary>
    private static bool IsOptionalUnsetSentinel(MemberAccessExpressionSyntax memberAccess)
    {
        if (memberAccess.Name.Identifier.ValueText != "Unset")
            return false;

        var receiverName = memberAccess.Expression switch
        {
            GenericNameSyntax generic => generic.Identifier.ValueText,            // Optional<T>.Unset
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,    // Optional.Unset
            MemberAccessExpressionSyntax qualified => qualified.Name.Identifier.ValueText, // Ns.Optional[<T>].Unset
            _ => null,
        };
        return receiverName == "Optional";
    }

    /// <summary>
    /// True when the callback is a lambda that provably discards the changed value:
    /// an empty block body, or a body that never references the lambda's parameter.
    /// Non-lambda callbacks (method groups, delegate variables) are never flagged —
    /// they may legitimately consume the value elsewhere.
    /// </summary>
    private static bool CallbackDropsValue(ExpressionSyntax expression)
    {
        expression = Unwrap(expression);
        return expression switch
        {
            SimpleLambdaExpressionSyntax simple =>
                LambdaBodyDropsValue(simple.Body, ParameterNames(simple.Parameter)),
            ParenthesizedLambdaExpressionSyntax paren =>
                LambdaBodyDropsValue(paren.Body, ParameterNames(paren.ParameterList.Parameters)),
            _ => false,
        };
    }

    private static bool LambdaBodyDropsValue(CSharpSyntaxNode body, ImmutableArray<string> parameterNames)
    {
        // An empty block body drops the value unconditionally.
        if (body is BlockSyntax { Statements.Count: 0 })
            return true;

        // No parameter to reference — an Action<T> callback always has one, but guard.
        if (parameterNames.IsEmpty)
            return false;

        // Fire only when the body never references the parameter by name. A lone '_'
        // single-parameter lambda is a usable parameter (C# treats a solitary '_' as
        // the parameter name, not a discard), so `_ => setName(_)` counts as a read.
        // A nested scope reusing the name over-matches into a false negative (safe:
        // never a false positive).
        foreach (var identifier in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (parameterNames.Contains(identifier.Identifier.ValueText))
                return false;
        }
        return true;
    }

    private static ImmutableArray<string> ParameterNames(ParameterSyntax parameter) =>
        ImmutableArray.Create(parameter.Identifier.ValueText);

    private static ImmutableArray<string> ParameterNames(SeparatedSyntaxList<ParameterSyntax> parameters) =>
        parameters.Select(p => p.Identifier.ValueText).ToImmutableArray();

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression) =>
        expression is ParenthesizedExpressionSyntax parenthesized
            ? Unwrap(parenthesized.Expression)
            : expression;
}
