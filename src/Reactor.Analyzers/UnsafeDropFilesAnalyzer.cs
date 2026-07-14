using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// REACTOR_INPUT_002: Detects <c>DragData.TryGetFiles(out ...)</c> called inside a
/// <c>.OnDrop(...)</c> handler. <c>TryGetFiles</c> returns whatever storage items the
/// drag source advertised — including UNC paths, DOS-device paths, reparse points, and
/// shell-virtual entries. An app that opens/parses/renders those files can be steered
/// into SMB/NTLM auth on a stat, out of the directory the user thought they shared, or
/// past a Mark-of-the-Web check. <c>TryGetSafeLocalFiles(out ...)</c> filters to safe
/// local paths, so the fix is a drop-in swap.
/// </summary>
/// <remarks>
/// Grounding: <c>src/Reactor/Input/DragData.cs</c> — <c>TryGetFiles</c> (raw) and
/// <c>TryGetSafeLocalFiles</c> (filtered) share the signature
/// <c>bool(out IReadOnlyList&lt;IStorageItem&gt;)</c>, so swapping the method name is a
/// compiling, behavior-preserving-for-safe-inputs change. The rule is scoped to the
/// <c>.OnDrop(...)</c> fluent modifier — the drop path where untrusted, source-chosen
/// files land.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeDropFilesAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_INPUT_002";

    /// <summary>The unsafe accessor the rule flags.</summary>
    internal const string UnsafeMethodName = "TryGetFiles";

    /// <summary>The safe accessor the code fix swaps in.</summary>
    internal const string SafeMethodName = "TryGetSafeLocalFiles";

    private const string DragDataTypeName = "DragData";
    private const string DragDataNamespace = "Microsoft.UI.Reactor.Input";

    // The drop-handler member: both the fluent `.OnDrop(...)` modifier and the raw
    // DropTargetConfig.OnDrop callback (DragConfigs.cs) carry this name.
    private const string OnDropHandlerName = "OnDrop";

    private static readonly LocalizableString Title =
        "Unsafe TryGetFiles in a drop handler; prefer TryGetSafeLocalFiles";

    private static readonly LocalizableString MessageFormat =
        "'TryGetFiles' in a drop handler returns UNC, DOS-device, reparse-point, and shell-virtual files chosen by the drag source; use 'TryGetSafeLocalFiles' to filter to safe local paths";

    private static readonly LocalizableString Description =
        "Dropped files come from another process that chose the paths. TryGetFiles returns " +
        "them verbatim: a UNC path triggers SMB/NTLM authentication on stat/open, a reparse " +
        "point can escape the directory the user thought they shared, and an Internet-zone " +
        "file loses its Mark-of-the-Web warning if the app reads bytes without consulting " +
        "Zone.Identifier. TryGetSafeLocalFiles filters those out and shares TryGetFiles' " +
        "signature, so an app that opens/parses/renders dropped files should call it instead.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Input",
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

        // Syntactic fast path: a `TryGetFiles(...)` member call — covers both `x.TryGetFiles(...)`
        // (member access) and `x?.TryGetFiles(...)` (conditional access / member binding).
        var invokedName = GetInvokedName(invocation);
        if (invokedName is null || invokedName.Identifier.Text != UnsafeMethodName)
            return;

        // Drop-context gate (syntactic): lexically inside a `.OnDrop(...)` handler.
        if (!IsInsideDropHandlerLambda(invocation))
            return;

        // Semantic confirm: the method resolves to Microsoft.UI.Reactor.Input.DragData.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
            return;
        var containingType = method.ContainingType;
        if (containingType is null || containingType.Name != DragDataTypeName)
            return;
        if (containingType.ContainingNamespace?.ToDisplayString() != DragDataNamespace)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
    }

    /// <summary>
    /// The invoked method-name node for a member invocation, covering both
    /// <c>x.TryGetFiles(...)</c> (<see cref="MemberAccessExpressionSyntax"/>) and
    /// <c>x?.TryGetFiles(...)</c> (<see cref="MemberBindingExpressionSyntax"/>, conditional
    /// access). Returns <c>null</c> for other call shapes. Shared by the analyzer and code fix so
    /// both agree on which call sites are in scope.
    /// </summary>
    internal static SimpleNameSyntax? GetInvokedName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            _ => null,
        };

    /// <summary>
    /// True when <paramref name="node"/> is lexically inside a drop handler — either a lambda /
    /// anonymous method passed to a <c>.OnDrop(...)</c> invocation (the fluent modifier) or one
    /// assigned to an <c>OnDrop</c> member (the raw <c>DropTargetConfig { OnDrop = ... }</c> /
    /// <c>with</c> / <c>cfg.OnDrop = ...</c> form). Walks every enclosing lambda/anonymous method
    /// so a call nested in an inner closure (e.g.
    /// <c>.OnDrop(a =&gt; list.ForEach(x =&gt; a.Data.TryGetFiles(...)))</c>) still counts — it
    /// runs during the drop.
    /// </summary>
    private static bool IsInsideDropHandlerLambda(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            // Covers lambdas (`args => ...`, `(args) => ...`) and anonymous methods
            // (`delegate(DragTargetArgs args) { ... }`).
            if (current is not AnonymousFunctionExpressionSyntax func)
                continue;

            // (A) handler argument to a `.OnDrop(...)` call — `el.OnDrop(...)` or the
            //     null-conditional `el?.OnDrop(...)` (GetInvokedName covers both shapes).
            if (func.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax outer } }
                && GetInvokedName(outer)?.Identifier.Text == OnDropHandlerName)
            {
                return true;
            }

            // (B) handler assigned to an `OnDrop` member: new DropTargetConfig { OnDrop = ... },
            //     cfg with { OnDrop = ... }, or cfg.OnDrop = ....
            if (func.Parent is AssignmentExpressionSyntax assignment && IsOnDropTarget(assignment))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsOnDropTarget(AssignmentExpressionSyntax assignment) => assignment.Left switch
    {
        // A bare `OnDrop = ...` counts only inside an object/with initializer
        // (`new DropTargetConfig { OnDrop = ... }` / `cfg with { OnDrop = ... }`), never a plain
        // local/field/property named OnDrop that is unrelated to a drop target.
        IdentifierNameSyntax id => id.Identifier.Text == OnDropHandlerName
            && assignment.Parent is InitializerExpressionSyntax,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text == OnDropHandlerName, // cfg.OnDrop = ...
        _ => false,
    };
}
