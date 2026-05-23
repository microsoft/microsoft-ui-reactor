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
/// Detects <c>TextField(...)</c>, <c>NumberBox(...)</c>, <c>PasswordBox(...)</c>,
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
        ImmutableHashSet.Create("TextBox", "TextField", "NumberBox", "PasswordBox", "AutoSuggestBox");

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

        // Check the fluent chain for .AutomationName() or .LabeledBy()
        if (HasModifierInChain(invocation, "AutomationName", "LabeledBy"))
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
