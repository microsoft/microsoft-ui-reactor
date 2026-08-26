using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_A11Y_001: Icon-only buttons need <c>.AutomationName()</c> for screen readers.
/// Detects <c>Button(icon, action)</c> where the first argument is not a string literal
/// and no <c>.AutomationName()</c> is present in the fluent chain.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IconButtonAccessibilityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_A11Y_001";

    private static readonly LocalizableString Title =
        "Icon-only button needs an accessible name";
    private static readonly LocalizableString MessageFormat =
        "Icon-only buttons need .AutomationName() for screen readers";
    private static readonly LocalizableString Description =
        "Buttons whose content is an icon or element (not a text string) must have " +
        ".AutomationName() so screen readers can announce them.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Microsoft.UI.Reactor.Accessibility",
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

    // <snippet:a11y-rule>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Match: Button(expr, action) as a factory call (IdentifierNameSyntax, not member access)
        if (invocation.Expression is not IdentifierNameSyntax identifier)
            return;
        if (identifier.Identifier.Text != "Button")
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 2)
            return;

        // If the first argument is a string literal, it's a text button — no diagnostic needed
        var firstArg = args[0].Expression;
        if (firstArg is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        // Check the fluent chain for .AutomationName()
        if (HasModifierInChain(invocation, "AutomationName"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation()));
    }
    // </snippet:a11y-rule>

    private static bool HasModifierInChain(SyntaxNode node, params string[] modifierNames)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is InvocationExpressionSyntax inv
                && inv.Expression is MemberAccessExpressionSyntax ma
                && modifierNames.Contains(ma.Name.Identifier.Text))
                return true;

            if (current is StatementSyntax or MemberDeclarationSyntax)
                break;

            current = current.Parent;
        }
        return false;
    }
}

/// <summary>
/// REACTOR_A11Y_002: Images need alt text or <c>.AccessibilityHidden()</c>.
/// Detects <c>Image(uri)</c> factory calls without <c>.AutomationName()</c> or
/// <c>.AccessibilityHidden()</c> in the fluent chain.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ImageAccessibilityAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_A11Y_002";

    private static readonly LocalizableString Title =
        "Image needs alt text or AccessibilityHidden()";
    private static readonly LocalizableString MessageFormat =
        "Images need .AutomationName() for alt text, or .AccessibilityHidden() if decorative";
    private static readonly LocalizableString Description =
        "Images must have an accessible name for screen readers, or be explicitly " +
        "marked as decorative with .AccessibilityHidden().";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Microsoft.UI.Reactor.Accessibility",
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

        if (invocation.Expression is not IdentifierNameSyntax identifier)
            return;
        if (identifier.Identifier.Text != "Image")
            return;

        // Check the fluent chain for .AutomationName() or .AccessibilityHidden()
        if (HasModifierInChain(invocation, "AutomationName", "AccessibilityHidden"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation()));
    }

    private static bool HasModifierInChain(SyntaxNode node, params string[] modifierNames)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is InvocationExpressionSyntax inv
                && inv.Expression is MemberAccessExpressionSyntax ma
                && modifierNames.Contains(ma.Name.Identifier.Text))
                return true;

            if (current is StatementSyntax or MemberDeclarationSyntax)
                break;

            current = current.Parent;
        }
        return false;
    }
}

/// <summary>
/// REACTOR_A11Y_003: Form fields need a label for screen readers.
/// Detects <c>TextBox(...)</c>, <c>NumberBox(...)</c>, <c>PasswordBox(...)</c>,
/// and <c>AutoSuggestBox(...)</c> factory calls without a <c>header:</c> named argument,
/// <c>.AutomationName()</c>, or <c>.LabeledBy()</c> in the fluent chain.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FormFieldLabelAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_A11Y_003";

    private static readonly LocalizableString Title =
        "Form field needs a label";
    private static readonly LocalizableString MessageFormat =
        "Form fields need a header, .AutomationName(), or .LabeledBy() for screen readers";
    private static readonly LocalizableString Description =
        "Form input fields must be labeled so screen readers can announce their purpose. " +
        "Use a header: argument, .AutomationName(), or .LabeledBy() to associate a label.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Microsoft.UI.Reactor.Accessibility",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    private static readonly ImmutableHashSet<string> FormFieldMethods =
        ImmutableHashSet.Create("TextBox", "NumberBox", "PasswordBox", "AutoSuggestBox");

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

        if (invocation.Expression is not IdentifierNameSyntax identifier)
            return;
        if (!FormFieldMethods.Contains(identifier.Identifier.Text))
            return;

        // Check for a named argument "header" or "Header"
        var args = invocation.ArgumentList.Arguments;
        foreach (var arg in args)
        {
            if (arg.NameColon is not null)
            {
                var name = arg.NameColon.Name.Identifier.Text;
                if (name == "header" || name == "Header")
                    return;
            }
        }

        // Check the fluent chain for a label-bearing modifier.
        //
        // `.Header(...)` counts: TextBox/ComboBox/Slider/ToggleSwitch/PasswordBox/AutoSuggestBox
        // all expose it (ElementExtensions.cs), and it renders a *visible* label that WinUI already
        // projects to the field's automation name. Omitting it here made the rule demand a
        // redundant `.AutomationName("Password")` next to a `.Header("Password")` that was doing
        // the job, which is worse guidance than the rule was written to give. The match is on the
        // exact identifier, so `PaneHeader` / `TabStripHeader` / `RightHeader` do not satisfy it.
        if (HasModifierInChain(invocation, "Header", "AutomationName", "LabeledBy"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation()));
    }

    private static bool HasModifierInChain(SyntaxNode node, params string[] modifierNames)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is InvocationExpressionSyntax inv
                && inv.Expression is MemberAccessExpressionSyntax ma
                && modifierNames.Contains(ma.Name.Identifier.Text))
                return true;

            if (current is StatementSyntax or MemberDeclarationSyntax)
                break;

            current = current.Parent;
        }
        return false;
    }
}

/// <summary>
/// REACTOR_A11Y_004: Clickable containers need keyboard focus.
/// Detects a non-focusable container factory (<c>Border</c>, <c>Grid</c>, <c>Canvas</c>,
/// <c>Rectangle</c>, <c>Ellipse</c>, <c>VStack</c>, <c>HStack</c>) carrying an actionable
/// <c>.OnTapped(...)</c> handler but no <c>.IsTabStop(true)</c> in the fluent chain. Such an
/// element is mouse/touch-hittable but skipped by Tab, so keyboard users can never reach it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClickableContainerKeyboardAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_A11Y_004";

    private static readonly LocalizableString Title =
        "Clickable container is not keyboard-focusable";
    private static readonly LocalizableString MessageFormat =
        "Clickable container has .OnTapped but is not keyboard-reachable; add .IsTabStop(true) " +
        "(and pair with .OnKeyDown for Enter/Space activation)";
    private static readonly LocalizableString Description =
        "A non-focusable container (Border, Grid, Canvas, Rectangle, Ellipse, VStack, HStack) with " +
        "a tap handler is hit-testable for pointer input but not in the keyboard tab order, so " +
        "keyboard users cannot reach it. Add .IsTabStop(true) to put it in the tab order, and pair " +
        "it with .OnKeyDown for Enter/Space activation.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Microsoft.UI.Reactor.Accessibility",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    /// <summary>
    /// Bare factory methods that produce a non-focusable WinUI container or shape. None of the
    /// backing types (Border → FrameworkElement, Grid/Canvas/StackPanel → Panel,
    /// Rectangle/Ellipse → Shape) derive from <c>Control</c>, so none is a tab stop by default —
    /// a tap handler on any of them is unreachable from the keyboard. Focus-bearing controls
    /// (Button, ScrollView, …) are deliberately excluded: they are already in the tab order.
    /// </summary>
    private static readonly ImmutableHashSet<string> NonFocusableContainerFactories =
        ImmutableHashSet.Create(
            "Border", "Grid", "Canvas", "Rectangle", "Ellipse", "VStack", "HStack");

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

        // Match a bare factory call — Border(...), Grid(...), etc. — as an IdentifierNameSyntax,
        // never a member access. That keeps the attached-layout modifier `.Grid(row:.., column:..)`
        // (always invoked on a receiver) out of the gate.
        if (invocation.Expression is not IdentifierNameSyntax identifier)
            return;
        if (!NonFocusableContainerFactories.Contains(identifier.Identifier.Text))
            return;

        var hasActionableTap = false;
        var hasFocusAffordance = false;

        // Inspect only the fluent chain applied directly to this factory result
        // (Border(x).A(..).OnTapped(..).B(..)); never ascend past the point where the chain
        // result is passed as an argument, so modifiers on an enclosing element are not
        // mis-attributed to this container.
        //
        // Suppress only when the chain enables the one affordance that actually makes these
        // non-Control containers keyboard-reachable: `.IsTabStop(true)` (the reconciler applies
        // IsTabStop to any FrameworkElement). `.TabIndex(n)` is a no-op here — the reconciler applies
        // it only to Controls — and `.OnKeyDown` does not add the element to the tab order, so
        // neither suppresses; `.IsTabStop(false)` explicitly opts out and must not suppress either.
        foreach (var modifier in EnumerateChainModifiers(invocation))
        {
            var name = modifier.MemberAccess.Name.Identifier.Text;
            if (name == "IsTabStop")
            {
                // Attached modifiers are last-wins, so a trailing .IsTabStop(false) overrides an
                // earlier .IsTabStop(true): the chain suppresses only if the final value enables it.
                hasFocusAffordance = !IsExplicitlyFalse(modifier.Invocation);
            }
            else if (name == "OnTapped" && !IsPureHandledSwallow(modifier.Invocation))
            {
                hasActionableTap = true;
            }
        }

        if (hasActionableTap && !hasFocusAffordance)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
        }
    }

    /// <summary>
    /// Yields each <c>receiver.Member(args)</c> invocation in the fluent chain applied to
    /// <paramref name="factory"/>, walking outward. Only genuine method-chain links
    /// (<c>factory.M(..).M2(..)</c>) are followed; enumeration stops as soon as the chain result
    /// becomes an argument or another expression's operand.
    /// </summary>
    private static IEnumerable<(MemberAccessExpressionSyntax MemberAccess, InvocationExpressionSyntax Invocation)>
        EnumerateChainModifiers(InvocationExpressionSyntax factory)
    {
        SyntaxNode current = factory;
        while (current.Parent is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression == current
            && memberAccess.Parent is InvocationExpressionSyntax outer
            && outer.Expression == memberAccess)
        {
            yield return (memberAccess, outer);
            current = outer;
        }
    }

    /// <summary>
    /// True when the call passes an explicit <c>false</c> as its first argument — e.g.
    /// <c>.IsTabStop(false)</c>, which turns the affordance OFF. Such a call must NOT suppress the
    /// diagnostic: it leaves the container out of the tab order, so it is still unreachable.
    /// <c>.IsTabStop()</c> (argument omitted, defaults to <c>true</c>) and <c>.IsTabStop(true)</c>
    /// both enable it and so do suppress.
    /// </summary>
    private static bool IsExplicitlyFalse(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        return args.Count >= 1 && args[0].Expression.IsKind(SyntaxKind.FalseLiteralExpression);
    }

    /// <summary>
    /// True when the tap handler does nothing but mark the event handled
    /// (<c>(_, e) =&gt; e.Handled = true</c>, or a block whose sole statement is that assignment).
    /// Such a handler is a pointer-event sink — e.g. a modal backdrop that blocks clicks reaching
    /// the content beneath — not an actionable command, so there is nothing to make
    /// keyboard-reachable. The assignment target must be the handler's own event-args parameter, so
    /// an unrelated <c>somethingElse.Handled = true</c> does not falsely suppress.
    /// </summary>
    private static bool IsPureHandledSwallow(InvocationExpressionSyntax onTapped)
    {
        var args = onTapped.ArgumentList.Arguments;
        if (args.Count != 1)
            return false;

        // The tap handler is Action<object, TappedRoutedEventArgs>; the event-args parameter (the
        // one carrying .Handled) is the last lambda parameter.
        IReadOnlyList<ParameterSyntax> parameters;
        CSharpSyntaxNode? body;
        switch (args[0].Expression)
        {
            case ParenthesizedLambdaExpressionSyntax p:
                parameters = p.ParameterList.Parameters;
                body = p.Body;
                break;
            case SimpleLambdaExpressionSyntax s:
                parameters = new[] { s.Parameter };
                body = s.Body;
                break;
            default:
                return false;
        }

        if (parameters.Count == 0 || body is null)
            return false;

        // A discard `_` can't be dereferenced, so it can't be the swallow shape.
        var eventArgsName = parameters[parameters.Count - 1].Identifier.Text;
        if (eventArgsName is "_" or "")
            return false;

        return body switch
        {
            AssignmentExpressionSyntax assign => IsHandledTrue(assign, eventArgsName),
            BlockSyntax block when block.Statements.Count == 1
                && block.Statements[0] is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax a }
                => IsHandledTrue(a, eventArgsName),
            _ => false,
        };
    }

    /// <summary>True for <c>&lt;eventArgsName&gt;.Handled = true</c>.</summary>
    private static bool IsHandledTrue(AssignmentExpressionSyntax assign, string eventArgsName) =>
        assign.IsKind(SyntaxKind.SimpleAssignmentExpression)
        && assign.Left is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax receiver,
            Name.Identifier.Text: "Handled"
        }
        && receiver.Identifier.Text == eventArgsName
        && assign.Right.IsKind(SyntaxKind.TrueLiteralExpression);
}
