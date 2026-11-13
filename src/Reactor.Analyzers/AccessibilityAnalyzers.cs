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
/// Detects <c>Button(icon, action)</c> where the first argument is not a string
/// (literal or <c>string</c>-typed expression, e.g. a localized resource lookup)
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

        // Button(string label, action) is a text button even when the label is not a
        // literal (for example a localized resource lookup); its content names it.
        if (IsStringTyped(firstArg, context))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation()));
    }

    private static bool IsStringTyped(ExpressionSyntax expression, SyntaxNodeAnalysisContext context)
    {
        // ConvertedType covers a label type with an implicit conversion to string that
        // binds to the Button(string, ...) overload.
        var info = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return info.Type?.SpecialType == SpecialType.System_String
            || info.ConvertedType?.SpecialType == SpecialType.System_String;
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
/// <c>AutoSuggestBox(...)</c>, <c>ComboBox(...)</c>, and <c>ToggleSwitch(...)</c> factory calls
/// without a header (argument, <c>.Header()</c>, or <c>with { Header = ... }</c>),
/// <c>.AutomationName()</c>, or <c>.LabeledBy()</c>. A <c>ToggleSwitch</c> is also labeled by its
/// on/off content, which WinUI's peer and Reactor's caption mirror both use as its name. A field
/// passed into a call that names it at runtime — <c>FormField(field, label: ...)</c>, a Community
/// Toolkit <c>SettingsCard(header: ..., content: field)</c>, or a source helper that applies a
/// label modifier to that parameter — is labeled too.
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
        "Use a header (header: argument or .Header()), .AutomationName(), or .LabeledBy() to " +
        "associate a label. A ToggleSwitch is also labeled by its on/off content.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Microsoft.UI.Reactor.Accessibility",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    // ComboBox and ToggleSwitch were added for issue #1007. A ComboBox's UIA Name comes only from
    // an explicit name or its Header — PlaceholderText feeds HelpText, not Name — so a placeholder
    // does not satisfy the rule for it (nor for any other field).
    private static readonly ImmutableHashSet<string> FormFieldMethods =
        ImmutableHashSet.Create(
            "TextBox", "NumberBox", "PasswordBox", "AutoSuggestBox", "ComboBox", "ToggleSwitch");

    private static readonly ImmutableHashSet<string> HeaderLabels =
        ImmutableHashSet.Create("header", "Header");

    // ToggleSwitchAutomationPeer names the switch from Header, then OnContent, then OffContent.
    private static readonly ImmutableHashSet<string> ToggleSwitchLabels =
        ImmutableHashSet.Create("header", "Header", "onContent", "OnContent", "offContent", "OffContent");

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
        var factory = identifier.Identifier.Text;
        if (!FormFieldMethods.Contains(factory))
            return;

        var labels = factory == "ToggleSwitch" ? ToggleSwitchLabels : HeaderLabels;

        if (HasLabelArgument(context, invocation, factory, labels))
            return;

        if (HasLabelInWithExpression(invocation, labels))
            return;

        // Check the fluent chain for a label-bearing modifier.
        //
        // `.Header(...)` counts: TextBox/ComboBox/Slider/ToggleSwitch/PasswordBox/AutoSuggestBox
        // all expose it (ElementExtensions.cs), and it renders a *visible* label that WinUI already
        // projects to the field's automation name. Omitting it here made the rule demand a
        // redundant `.AutomationName("Password")` next to a `.Header("Password")` that was doing
        // the job, which is worse guidance than the rule was written to give. The match is on the
        // exact identifier, so `PaneHeader` / `TabStripHeader` / `RightHeader` do not satisfy it.
        if (HasModifierInChain(invocation, LabelModifiers))
            return;

        if (IsLabeledByEnclosingCall(context, invocation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation()));
    }

    private static readonly string[] LabelModifiers = { "Header", "AutomationName", "LabeledBy" };

    /// <summary>
    /// True when a label-bearing parameter (<paramref name="labels"/>) receives a non-null argument,
    /// whether named or positional. Positional arguments are mapped through the bound method so
    /// <c>TextBox(v, set, "placeholder", "Header")</c> counts; when binding fails (dynamic
    /// arguments, mid-edit code) the Reactor factory layout is assumed instead.
    /// </summary>
    private static bool HasLabelArgument(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string factory,
        ImmutableHashSet<string> labels)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
            return false;

        IMethodSymbol? method = null;
        var resolved = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (IsNullLiteral(arg.Expression))
                continue;

            if (arg.NameColon is null && !resolved)
            {
                method = ResolveMethod(context, invocation);
                resolved = true;
            }

            var parameterName = ParameterName(arg, i, method)
                ?? (method is null ? ReactorPositionalParameter(factory, i) : null);
            if (parameterName is not null && labels.Contains(parameterName))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when the field (with any fluent chain) is passed straight into a call that labels it:
    /// <list type="bullet">
    /// <item>a known labeling container (<see cref="KnownContainerLabelParameter"/>) such as
    /// Reactor's <c>FormField(content, label: "...")</c>, given a non-null label;</item>
    /// <item>a helper declared in source whose body applies <c>.AutomationName</c>,
    /// <c>.Header</c>, or <c>.LabeledBy</c> to the very parameter the field is bound to — e.g. a
    /// <c>SettingsRow(string label, Element control) =&gt; HStack(TextBlock(label),
    /// control.AutomationName(label))</c>.</item>
    /// </list>
    /// A plain container (<c>VStack</c>, <c>HStack</c>, …) is neither, so a field merely sitting
    /// next to a caption <c>TextBlock</c> is still reported.
    /// </summary>
    private static bool IsLabeledByEnclosingCall(
        SyntaxNodeAnalysisContext context, InvocationExpressionSyntax field)
    {
        SyntaxNode node = field;
        while (true)
        {
            var parent = node.Parent;
            if (parent is MemberAccessExpressionSyntax ma && ma.Expression == node
                && parent.Parent is InvocationExpressionSyntax chained && chained.Expression == ma)
            {
                node = chained;
                continue;
            }
            if (parent is ParenthesizedExpressionSyntax or CastExpressionSyntax
                || parent is WithExpressionSyntax withExpr && withExpr.Expression == node)
            {
                node = parent;
                continue;
            }
            break;
        }

        if (node.Parent is not ArgumentSyntax
            {
                Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list,
            } argument)
            return false;

        var method = ResolveMethod(context, call);
        if (method is null)
            return false;

        var parameterName = ParameterName(argument, list.Arguments.IndexOf(argument), method);
        if (parameterName is null)
            return false;

        if (KnownContainerLabelParameter(method) is { } labelParameter)
        {
            if (parameterName != "content")
                return false;
            var args = list.Arguments;
            for (var i = 0; i < args.Count; i++)
            {
                if (ParameterName(args[i], i, method) == labelParameter && !IsNullLiteral(args[i].Expression))
                    return true;
            }
            return false;
        }

        return ForwardsLabel(method.ReducedFrom ?? method.OriginalDefinition, parameterName, depth: 0, context.CancellationToken);
    }

    /// <summary>
    /// True when <paramref name="method"/>'s source body applies a label modifier to
    /// <paramref name="parameterName"/>, directly or by handing it to another helper on the same
    /// type that does (e.g. <c>OptionPanel(params Element[] options)</c> →
    /// <c>NameOptionsFromCaptions(options)</c> → <c>options[i].AutomationName(...)</c>). Inner
    /// helpers are found by name on the containing type rather than through a semantic model, and
    /// the walk is bounded so a recursive helper cannot loop.
    /// </summary>
    private static bool ForwardsLabel(
        IMethodSymbol method, string parameterName, int depth, System.Threading.CancellationToken ct)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var body = reference.GetSyntax(ct) switch
            {
                MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                LocalFunctionStatementSyntax l => (SyntaxNode?)l.Body ?? l.ExpressionBody,
                _ => null,
            };
            if (body is null)
                continue;

            foreach (var inner in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (inner.Expression is MemberAccessExpressionSyntax labelCall
                    && LabelModifiers.Contains(labelCall.Name.Identifier.Text)
                    && ReceiverRoot(labelCall.Expression) is IdentifierNameSyntax receiver
                    && receiver.Identifier.Text == parameterName)
                    return true;

                if (depth < MaxForwardingDepth
                    && inner.Expression is IdentifierNameSyntax helperName
                    && method.ContainingType is { } type)
                {
                    var args = inner.ArgumentList.Arguments;
                    for (var i = 0; i < args.Count; i++)
                    {
                        if (args[i].Expression is not IdentifierNameSyntax passed
                            || passed.Identifier.Text != parameterName)
                            continue;

                        foreach (var helper in type.GetMembers(helperName.Identifier.Text).OfType<IMethodSymbol>())
                        {
                            if (ParameterName(args[i], i, helper) is { } next
                                && ForwardsLabel(helper, next, depth + 1, ct))
                                return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    private const int MaxForwardingDepth = 3;

    /// <summary>
    /// For a container that names its <c>content</c> from one of its own arguments at runtime,
    /// the name of that argument's parameter; otherwise null.
    /// <list type="bullet">
    /// <item>Reactor's <c>FormField(content, label)</c> — <c>ApplyFormFieldAutomation</c> sets the
    /// content's <c>AutomationProperties.Name</c> from the label.</item>
    /// <item>A <c>[GenerateReactorWrapper]</c> factory for the Community Toolkit
    /// <c>SettingsCard</c> — its <c>SetAccessibleContentName</c> names the content from a string
    /// <c>Header</c>.</item>
    /// </list>
    /// </summary>
    private static string? KnownContainerLabelParameter(IMethodSymbol method)
    {
        if (method.Name == "FormField"
            && CommandDebounceAnalyzer.IsReactorNamespace(method.ContainingNamespace?.ToDisplayString()))
            return "label";

        if (method.Name == "SettingsCard" && method.ContainingType is { } type)
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.Name == "GenerateReactorWrapperAttribute"
                    && attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is ITypeSymbol wrapped
                    && wrapped.ToDisplayString() == "CommunityToolkit.WinUI.Controls.SettingsCard")
                    return "header";
            }
        }
        return null;
    }

    /// <summary>
    /// The identifier a label call is ultimately made on: <c>control</c> for
    /// <c>control.AutomationName(...)</c>, <c>options</c> for <c>options[i].AutomationName(...)</c>.
    /// </summary>
    private static ExpressionSyntax ReceiverRoot(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax p: expression = p.Expression; break;
                case CastExpressionSyntax c: expression = c.Expression; break;
                case ElementAccessExpressionSyntax e: expression = e.Expression; break;
                case PostfixUnaryExpressionSyntax u when u.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = u.Operand; break;
                default: return expression;
            }
        }
    }

    private static IMethodSymbol? ResolveMethod(
        SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        var info = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        return info.Symbol as IMethodSymbol
            ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] as IMethodSymbol : null);
    }

    /// <summary>The parameter an argument binds to: by name if named, else by position.</summary>
    private static string? ParameterName(ArgumentSyntax argument, int index, IMethodSymbol? method)
    {
        if (argument.NameColon is not null)
            return argument.NameColon.Name.Identifier.Text;
        if (method is null || method.Parameters.Length == 0)
            return null;
        if (index < method.Parameters.Length)
            return method.Parameters[index].Name;
        var last = method.Parameters[method.Parameters.Length - 1];
        return last.IsParams ? last.Name : null;
    }

    /// <summary>Label-bearing positional slots of the Reactor factories (Dsl.cs).</summary>
    private static string? ReactorPositionalParameter(string factory, int index) => (factory, index) switch
    {
        ("TextBox", 3) => "header",
        ("NumberBox", 2) => "header",
        ("ToggleSwitch", 2) => "onContent",
        ("ToggleSwitch", 3) => "offContent",
        ("ToggleSwitch", 4) => "header",
        _ => null,
    };

    /// <summary>
    /// True for <c>(Field(...) with { Header = "..." })</c> — the record-initializer spelling of a
    /// header, which the docs use for controls without a fluent <c>.Header()</c>.
    /// </summary>
    private static bool HasLabelInWithExpression(SyntaxNode node, ImmutableHashSet<string> labels)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is WithExpressionSyntax withExpr && withExpr.Expression.Span.Contains(node.Span))
            {
                foreach (var expression in withExpr.Initializer.Expressions)
                {
                    if (expression is AssignmentExpressionSyntax
                        {
                            Left: IdentifierNameSyntax property,
                        } assignment
                        && labels.Contains(property.Identifier.Text)
                        && !IsNullLiteral(assignment.Right))
                        return true;
                }
            }

            if (current is StatementSyntax or MemberDeclarationSyntax)
                break;

            current = current.Parent;
        }
        return false;
    }

    private static bool IsNullLiteral(ExpressionSyntax expression) =>
        expression.IsKind(SyntaxKind.NullLiteralExpression)
        || expression.IsKind(SyntaxKind.DefaultLiteralExpression)
        || expression.IsKind(SyntaxKind.DefaultExpression);

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
