using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Shared syntactic/semantic helpers for the <c>.Set(x =&gt; x.Member = value)</c>
/// family of analyzers (<c>REACTOR_POOL_001</c>, <c>REACTOR_ITEMS_001</c>,
/// <c>REACTOR_CTRL_001</c>, <c>REACTOR_VIS_001</c>, <c>REACTOR_EVENT_001</c>).
/// </summary>
/// <remarks>
/// The Reactor DSL exposes a strongly-typed <c>.Set(this XElement, Action&lt;WinUIControl&gt;)</c>
/// per element type (<c>ElementExtensions.cs</c>). Every rule in this family starts
/// from the same syntactic shape — a single-argument <c>.Set</c> whose lambda body is
/// one assignment/compound-assignment against the native control — and then layers a
/// rule-specific member/type check on top. This helper centralizes that shared shape so
/// the analyzers can't drift apart (see spec 060 §3.1).
/// </remarks>
internal static class SetLambdaHelpers
{
    /// <summary>
    /// Reactor collection elements whose items are owned by keyed reconciliation, so a
    /// manual <c>.Set(x =&gt; x.ItemsSource = ...)</c> fights the diff (REACTOR_ITEMS_001).
    /// <c>AutoSuggestBoxElement</c> is deliberately excluded — there, <c>ItemsSource</c>
    /// is the documented escape hatch.
    /// </summary>
    internal static readonly ImmutableHashSet<string> OwnedItemsSourceElements =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ListViewElement",
            "GridViewElement",
            "TreeViewElement",
            "TabViewElement",
            "PivotElement",
            "FlipViewElement",
            "SelectorBarElement");

    /// <summary>
    /// Reactor selector elements whose selection is controlled through
    /// <c>Optional&lt;int&gt; SelectedIndex</c>; a manual
    /// <c>.Set(x =&gt; x.SelectedItem = ...)</c> creates a competing authority
    /// (REACTOR_CTRL_001). <c>NavigationViewElement</c> is intentionally absent — it
    /// selects by <c>SelectedTag</c>, an element property, not a WinUI control member.
    /// </summary>
    internal static readonly ImmutableHashSet<string> SelectedIndexControlledElements =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ComboBoxElement",
            "RadioButtonsElement",
            "ListViewElement",
            "GridViewElement");

    /// <summary>
    /// Matches a <c>receiver.Set(lambda)</c> invocation (single argument). Returns the
    /// <see cref="MemberAccessExpressionSyntax"/> whose <see cref="MemberAccessExpressionSyntax.Expression"/>
    /// is the element receiver. Purely syntactic — the cheap fast-path gate every rule runs first.
    /// </summary>
    internal static bool IsSetInvocation(
        InvocationExpressionSyntax invocation,
        out MemberAccessExpressionSyntax memberAccess)
    {
        memberAccess = null!;
        if (invocation.Expression is not MemberAccessExpressionSyntax ma)
            return false;
        if (ma.Name.Identifier.Text != "Set")
            return false;
        if (invocation.ArgumentList.Arguments.Count != 1)
            return false;
        memberAccess = ma;
        return true;
    }

    /// <summary>
    /// Extract the single assignment expression from a lambda passed to <c>.Set(...)</c>.
    /// Supports both expression-body lambdas (<c>fe =&gt; fe.X = v</c>) and block-body
    /// lambdas with a single assignment statement (<c>fe =&gt; { fe.X = v; }</c>).
    /// Multi-statement blocks return <c>null</c> — a codefix can't safely rewrite them.
    /// Returns simple assignments (<c>=</c>) and compound assignments (<c>+=</c>/<c>-=</c>);
    /// callers branch on <see cref="AssignmentExpressionSyntax.Kind"/>.
    /// </summary>
    internal static AssignmentExpressionSyntax? TryGetLambdaAssignment(ExpressionSyntax lambdaExpr)
    {
        SyntaxNode? exprOrBlock = lambdaExpr switch
        {
            SimpleLambdaExpressionSyntax simple => (SyntaxNode?)simple.ExpressionBody ?? simple.Block,
            ParenthesizedLambdaExpressionSyntax paren => (SyntaxNode?)paren.ExpressionBody ?? paren.Block,
            _ => null,
        };

        return exprOrBlock switch
        {
            AssignmentExpressionSyntax a => a,
            BlockSyntax block when block.Statements.Count == 1
                && block.Statements[0] is ExpressionStatementSyntax es
                && es.Expression is AssignmentExpressionSyntax ba => ba,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the single parameter of a <c>.Set</c> lambda (<c>x</c> in
    /// <c>x =&gt; x.M = v</c>), or <c>null</c> for zero/multi-parameter lambdas.
    /// </summary>
    internal static ParameterSyntax? GetSingleLambdaParameter(ExpressionSyntax lambdaExpr) =>
        lambdaExpr switch
        {
            SimpleLambdaExpressionSyntax s => s.Parameter,
            ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: { Count: 1 } ps } => ps[0],
            _ => null,
        };

    /// <summary>
    /// When the assignment's left side is <c>&lt;paramName&gt;.Member</c> (the lambda
    /// parameter's own member), returns that member-access node; otherwise <c>null</c>.
    /// Passing <c>paramName == null</c> skips the receiver-identity check and accepts any
    /// member-access left side (POOL_001's historical behavior).
    /// </summary>
    internal static MemberAccessExpressionSyntax? GetAssignedMemberAccess(
        AssignmentExpressionSyntax assignment, string? paramName)
    {
        if (assignment.Left is not MemberAccessExpressionSyntax leftAccess)
            return null;
        if (paramName is not null &&
            !(leftAccess.Expression is IdentifierNameSyntax id && id.Identifier.Text == paramName))
            return null;
        return leftAccess;
    }

    /// <summary>
    /// Confirms the matched <c>.Set(...)</c> invocation resolves to a Reactor DSL setter —
    /// an extension method under the <c>Microsoft.UI.Reactor</c> namespace root
    /// (<c>ElementExtensions.cs</c>). The whole family's diagnostics and fluent-modifier code
    /// fixes only make sense for Reactor elements, so this keeps them from firing (and
    /// offering uncompilable fixes) on an unrelated user-defined <c>.Set</c> helper that
    /// happens to share the syntactic shape.
    /// </summary>
    internal static bool IsReactorSetInvocation(
        InvocationExpressionSyntax invocation, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
            return false;
        var ns = method.ContainingNamespace?.ToDisplayString();
        return ns is not null &&
            (ns == "Microsoft.UI.Reactor" || ns.StartsWith("Microsoft.UI.Reactor.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Walks the base-type chain checking for a type with the given simple name in the
    /// given namespace (e.g. <c>UIElement</c> / <c>FrameworkElement</c> in
    /// <c>Microsoft.UI.Xaml</c>).
    /// </summary>
    internal static bool InheritsFrom(ITypeSymbol? type, string simpleName, string @namespace)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name == simpleName &&
                current.ContainingNamespace?.ToDisplayString() == @namespace)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="type"/> is one of the curated Reactor element names AND
    /// lives under the <c>Microsoft.UI.Reactor</c> namespace root — the syntactic curated
    /// table plus a namespace guard against unrelated same-named types.
    /// </summary>
    internal static bool IsCuratedReactorElement(ITypeSymbol? type, ImmutableHashSet<string> curatedNames)
    {
        if (type is null || !curatedNames.Contains(type.Name))
            return false;
        var ns = type.ContainingNamespace?.ToDisplayString();
        return ns is not null &&
            (ns == "Microsoft.UI.Reactor" || ns.StartsWith("Microsoft.UI.Reactor.", StringComparison.Ordinal));
    }
}
