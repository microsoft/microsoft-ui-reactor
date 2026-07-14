using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_DIALOG_001</c> — flags a WinUI <c>ContentDialog</c> shown imperatively with
/// <c>.ShowAsync()</c> (typically from an event handler), instead of the controlled Reactor
/// <c>ContentDialog(...)</c> element driven by its <c>IsOpen</c> property.
/// </summary>
/// <remarks>
/// <para>
/// A <c>new ContentDialog { … }.ShowAsync()</c> materializes the dialog outside the Reactor
/// virtual element tree: it inherits no parent theme, can't be driven by a controlled
/// <c>IsOpen</c> flag, can't share component state, and can't be exercised by the renderer test
/// fixture. The declarative pattern keeps the dialog in the tree at all times and toggles
/// visibility through <c>IsOpen</c> (with <c>OnClosed</c> flipping it back on dismissal) — see
/// <c>docs/guide/dialogs-and-flyouts.md</c> "Common Mistakes".
/// </para>
/// <para>
/// Detection anchors on the imperative open — the <c>ShowAsync()</c> invocation — because that
/// is the harmful action the rule targets and it appears exactly once per misused dialog, so a
/// single anchor catches every documented form (inline <c>new ContentDialog{…}.ShowAsync()</c>
/// and the two-statement <c>var d = new ContentDialog{…}; await d.ShowAsync();</c>) without ever
/// double-reporting. A cheap syntactic gate matches the invoked member name <c>ShowAsync</c>
/// (member-access, conditional-access, or an implicit-receiver identifier), then one
/// <c>GetSymbolInfo</c> confirms the invoked method belongs to the WinUI
/// <c>Microsoft.UI.Xaml.Controls.ContentDialog</c> type <b>or a subclass of it</b>. Reactor's own
/// <c>ContentDialog(...)</c> DSL factory is an invocation named <c>ContentDialog</c>, not
/// <c>ShowAsync</c>, so the correct controlled path is filtered out by the name gate and never
/// reaches the semantic check.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ImperativeContentDialogAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_DIALOG_001";

    private const string ShowAsyncName = "ShowAsync";
    private const string ContentDialogTypeName = "ContentDialog";
    private const string WinUiControlsNamespace = "Microsoft.UI.Xaml.Controls";

    private static readonly LocalizableString Title =
        "Imperative ContentDialog.ShowAsync escapes the Reactor render tree";

    private static readonly LocalizableString MessageFormat =
        "'ContentDialog.ShowAsync()' opens a dialog imperatively, outside the Reactor render tree; " +
        "use the controlled 'ContentDialog(...)' element with 'IsOpen' instead.";

    private static readonly LocalizableString Description =
        "A WinUI ContentDialog shown with ShowAsync() from an event handler is created outside the " +
        "virtual element tree: it inherits no parent theme, can't be driven by a controlled IsOpen " +
        "flag, can't share component state, and can't be exercised by the renderer test fixture. " +
        "Model the dialog declaratively with the Reactor ContentDialog(...) factory and toggle its " +
        "IsOpen property from state (OnClosed flips it back on dismissal).";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Lifecycle",
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

        // Cheap syntactic gate: `<receiver>.ShowAsync(...)`, `<receiver>?.ShowAsync(...)`, or a
        // bare `ShowAsync(...)` with an implicit receiver (e.g. inside a ContentDialog subclass).
        // The Reactor controlled path — the `ContentDialog(...)` DSL factory — is an invocation
        // named `ContentDialog`, not `ShowAsync`, so it can never reach the semantic check below.
        // Any argument count matches (ShowAsync() and the ShowAsync(ContentDialogPlacement)
        // overload). ValueText (not Text) so an escaped call like `dialog.@ShowAsync()` still
        // matches the "ShowAsync" identifier.
        var invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText, // dialog?.ShowAsync()
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,          // implicit receiver
            _ => null,
        };
        if (invokedName != ShowAsyncName)
            return;

        // Semantic confirmation: the invoked ShowAsync must resolve to a method on the WinUI
        // ContentDialog type (or a subclass of it). `ShowAsync` normally lives on ContentDialog
        // itself, so the resolved method's ContainingType is ContentDialog even for subclass
        // receivers; walking the base-type chain additionally catches a subclass that hides/declares
        // its own ShowAsync (ContainingType = the derived type). This is what separates the real
        // anti-pattern from unrelated `ShowAsync` methods (MessageDialog, custom types,
        // delegate-typed members, …). If the symbol can't be resolved (incomplete code mid-edit),
        // stay silent — this is a no-autofix Warning and firing on an unconfirmed receiver would be
        // a false positive.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method)
            return;
        if (!IsWinUiContentDialogOrSubclass(method.ContainingType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
    }

    // True when <paramref name="type"/> is the WinUI ContentDialog or derives from it. Walking the
    // base-type chain (the pattern used by ReferenceCurrentReadAnalyzer) catches a subclass that
    // declares its own ShowAsync — its ContainingType is the subclass, not ContentDialog.
    private static bool IsWinUiContentDialogOrSubclass(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name == ContentDialogTypeName
                && current.ContainingNamespace?.ToDisplayString() == WinUiControlsNamespace)
                return true;
        }

        return false;
    }
}
