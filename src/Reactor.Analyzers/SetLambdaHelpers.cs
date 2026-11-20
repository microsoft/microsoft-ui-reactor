using System;
using System.Collections.Immutable;
using System.Linq;
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
    /// All assignment expressions in a <c>.Set(...)</c> lambda body — the expression body
    /// (<c>fe =&gt; fe.X = v</c>) or every top-level assignment statement in a block body,
    /// regardless of statement count.
    /// <para>
    /// Prefer this over <see cref="TryGetLambdaAssignment"/> for pure <em>detection</em>.
    /// The single-assignment helper deliberately bails on multi-statement blocks because a
    /// code fix cannot mechanically rewrite them — but a diagnostic still should fire.
    /// Reusing the code-fix-shaped helper for detection created a false negative that hid
    /// real double-subscribe bugs inside bodies like
    /// <c>.Set(ib =&gt; { ib.IsOpen = true; ib.Closed += h; })</c>, where the offending
    /// <c>+=</c> is merely sharing a block with another statement.
    /// </para>
    /// </summary>
    internal static ImmutableArray<AssignmentExpressionSyntax> GetLambdaAssignments(ExpressionSyntax lambdaExpr)
    {
        switch (GetLambdaBody(lambdaExpr))
        {
            case AssignmentExpressionSyntax a:
                return ImmutableArray.Create(a);
            case BlockSyntax block:
            {
                // OfType/Select rather than a foreach with an inner `is` test: makes the
                // filter explicit (CodeQL cs/linq/missed-where) without the double type-test
                // a Where(...) + cast would need, since the pattern here also binds.
                var assignments = block.Statements
                    .OfType<ExpressionStatementSyntax>()
                    .Select(statement => statement.Expression)
                    .OfType<AssignmentExpressionSyntax>();

                var builder = ImmutableArray.CreateBuilder<AssignmentExpressionSyntax>();
                foreach (var assignment in assignments)
                    builder.Add(assignment);
                return builder.ToImmutable();
            }
            default:
                return ImmutableArray<AssignmentExpressionSyntax>.Empty;
        }
    }

    /// <summary>
    /// The expression body or block body of a lambda, or <c>null</c> when the node is not a
    /// lambda at all.
    /// </summary>
    internal static SyntaxNode? GetLambdaBody(ExpressionSyntax lambdaExpr) =>
        lambdaExpr switch
        {
            SimpleLambdaExpressionSyntax simple => (SyntaxNode?)simple.ExpressionBody ?? simple.Block,
            ParenthesizedLambdaExpressionSyntax paren => (SyntaxNode?)paren.ExpressionBody ?? paren.Block,
            _ => null,
        };

    /// <summary>
    /// All top-level invocation expressions in a <c>.Set(...)</c> lambda body — the
    /// expression body (<c>fe =&gt; Owner.SetX(fe, v)</c>) or every top-level expression
    /// statement in a block body.
    /// </summary>
    /// <remarks>
    /// The invocation counterpart of <see cref="GetLambdaAssignments"/>, and deliberately
    /// scoped the same way: top-level statements only. An attached write nested inside an
    /// <c>if</c> or a loop is equally lost on pool reuse, but matching those would report a
    /// diagnostic on a body no code fix can convert, and the enclosing statement is where a
    /// reader would look anyway.
    /// </remarks>
    internal static ImmutableArray<InvocationExpressionSyntax> GetLambdaInvocations(ExpressionSyntax lambdaExpr)
    {
        switch (GetLambdaBody(lambdaExpr))
        {
            case InvocationExpressionSyntax invocation:
                return ImmutableArray.Create(invocation);
            case BlockSyntax block:
            {
                var builder = ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();
                foreach (var invocation in block.Statements
                    .OfType<ExpressionStatementSyntax>()
                    .Select(statement => statement.Expression)
                    .OfType<InvocationExpressionSyntax>())
                {
                    builder.Add(invocation);
                }
                return builder.ToImmutable();
            }
            default:
                return ImmutableArray<InvocationExpressionSyntax>.Empty;
        }
    }

    /// <summary>
    /// Matches an attached-property setter call whose target is the <c>.Set</c> lambda's own
    /// parameter — <c>Owner.SetPROP(&lt;paramName&gt;, value)</c>. Returns <c>false</c> for
    /// any other shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The target check is the load-bearing one, and the invocation-shape mirror of what
    /// <see cref="GetAssignedMemberAccess"/> does for the assignment shape. Without it,
    /// <c>.Set(panel =&gt; Grid.SetRow(panel.Children[0], 1))</c> and
    /// <c>.Set(fe =&gt; AutomationProperties.SetName(captured, "x"))</c> would both report —
    /// neither writes to the pooled control the <c>.Set</c> configures.
    /// </para>
    /// <para>
    /// Parentheses and casts around the target are seen through: <c>(UIElement)c</c> is still
    /// <c>c</c>, and the cast is common because the WinUI setters are typed on
    /// <c>DependencyObject</c>/<c>UIElement</c>. Nothing else is unwrapped — a conditional or
    /// a method call could name a different object.
    /// </para>
    /// <para>
    /// Purely syntactic; the caller pins <paramref name="ownerName"/> to its expected
    /// namespace through the semantic model, because a user type may share the simple name.
    /// </para>
    /// </remarks>
    internal static bool TryMatchAttachedSetterCall(
        InvocationExpressionSyntax invocation,
        string paramName,
        out string ownerName,
        out string setterName,
        out ExpressionSyntax value)
    {
        ownerName = null!;
        setterName = null!;
        value = null!;

        // Owner.SetPROP(target, value) — an unqualified SetPROP(...) has no owner to key on,
        // and a deeper qualification (Microsoft.UI.Xaml.Automation.AutomationProperties) is
        // handled by taking the rightmost name.
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
            return false;

        var owner = access.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax qualified => qualified.Name.Identifier.Text,
            AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.Text,
            _ => null,
        };
        if (owner is null)
            return false;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 2)
            return false;

        // Named arguments are rejected rather than bound: they may be written out of order
        // (`SetName(value: "x", element: fe)`), which would make the positional target/value
        // read below silently wrong. A missed diagnostic on a rare shape is the safe side of
        // that trade. ref/out cannot appear on these setters but would break the same read.
        if (arguments.Any(argument =>
            argument.NameColon is not null || !argument.RefKindKeyword.IsKind(SyntaxKind.None)))
        {
            return false;
        }

        if (!IsLambdaParameterReference(arguments[0].Expression, paramName))
            return false;

        ownerName = owner;
        setterName = access.Name.Identifier.Text;
        value = arguments[1].Expression;
        return true;
    }

    /// <summary>
    /// True when <paramref name="expression"/> is the lambda parameter, looking through
    /// parentheses, casts and the null-forgiving operator — none of which change which object
    /// is being written to.
    /// </summary>
    private static bool IsLambdaParameterReference(ExpressionSyntax expression, string paramName)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax suppression
                    when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = suppression.Operand;
                    continue;
                default:
                    return expression is IdentifierNameSyntax id
                        && string.Equals(id.Identifier.Text, paramName, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Confirms an attached setter really belongs to <paramref name="expectedNamespace"/>.
    /// </summary>
    /// <remarks>
    /// The simple owner name is not enough on its own: a user type called
    /// <c>AutomationProperties</c> with a two-argument <c>SetName</c> would otherwise be
    /// rewritten to a Reactor modifier that has nothing to do with it. Resolved through the
    /// semantic model only after the syntactic gates pass, for the same cost reason
    /// <see cref="IsReactorSetInvocation"/> is resolved lazily.
    /// </remarks>
    internal static bool IsAttachedSetterInNamespace(
        InvocationExpressionSyntax invocation,
        string expectedOwner,
        string expectedNamespace,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
            return false;
        if (!method.IsStatic)
            return false;

        var owner = method.ContainingType;
        return owner is not null
            && string.Equals(owner.Name, expectedOwner, StringComparison.Ordinal)
            && owner.ContainingNamespace?.ToDisplayString() == expectedNamespace;
    }

    /// <summary>
    /// One write inside a <c>.Set(...)</c> body: either an instance-property assignment
    /// (<c>fe.PROP = v</c>) or an attached-property setter call
    /// (<c>Owner.SetPROP(fe, v)</c>). Exactly one of the two is non-null.
    /// </summary>
    internal readonly struct SetBodyWrite
    {
        private SetBodyWrite(
            AssignmentExpressionSyntax? assignment,
            InvocationExpressionSyntax? attachedCall,
            string key,
            ExpressionSyntax value)
        {
            Assignment = assignment;
            AttachedCall = attachedCall;
            Key = key;
            Value = value;
        }

        public AssignmentExpressionSyntax? Assignment { get; }

        public InvocationExpressionSyntax? AttachedCall { get; }

        /// <summary>
        /// How the analyzer named this write in its diagnostic: the bare property name for an
        /// assignment, <c>Owner.Property</c> for an attached call. Matched against the
        /// analyzer's reported/fixable property bags.
        /// </summary>
        public string Key { get; }

        /// <summary>The value being written — the modifier's argument after rewriting.</summary>
        public ExpressionSyntax Value { get; }

        /// <summary>The statement or expression the write's trivia hangs off.</summary>
        public SyntaxNode Node => (SyntaxNode?)Assignment ?? AttachedCall!;

        public static SetBodyWrite FromAssignment(AssignmentExpressionSyntax assignment) =>
            new(assignment,
                null,
                ((MemberAccessExpressionSyntax)assignment.Left).Name.Identifier.Text,
                assignment.Right);

        public static SetBodyWrite FromAttachedCall(
            InvocationExpressionSyntax invocation, string key, ExpressionSyntax value) =>
            new(null, invocation, key, value);
    }

    /// <summary>
    /// Extract the writes from a <c>.Set(...)</c> lambda when — and only when — the entire
    /// body can be replaced by a modifier chain: every statement is either a simple
    /// assignment whose receiver is the lambda parameter itself, or an attached-property
    /// setter call whose target is that parameter.
    /// Returns empty when the body is not fully convertible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately stricter than <see cref="GetLambdaAssignments"/> /
    /// <see cref="GetLambdaInvocations"/>, which exist for <em>detection</em> and may ignore
    /// statements they cannot classify. A code fix cannot ignore them: it replaces the whole
    /// invocation, so anything unaccounted for is silently deleted rather than left behind.
    /// Three shapes must be rejected:
    /// </para>
    /// <list type="bullet">
    /// <item><description>A statement that is neither shape (<c>c.Focus();</c>, an <c>if</c>, a
    /// local declaration). Invisible to a filter over assignments, and destroyed by the
    /// rewrite.</description></item>
    /// <item><description>An assignment to a different receiver (<c>other.IsEnabled = true</c>).
    /// The analyzer only reports writes to the lambda parameter, but it reports them by
    /// property <em>name</em> — so a same-named write to a captured variable would otherwise
    /// be folded into the chain and lost.</description></item>
    /// <item><description>An attached setter call against a different target
    /// (<c>Grid.SetRow(panel.Children[0], 1)</c>), for the same reason.</description></item>
    /// </list>
    /// <para>
    /// Whether each write's <em>property</em> is convertible is the caller's job — this only
    /// establishes that every statement is accounted for. Comments inside the block body are
    /// not carried over; trivia around the invocation is.
    /// </para>
    /// </remarks>
    internal static ImmutableArray<SetBodyWrite> GetFullyConvertibleLambdaBody(
        ExpressionSyntax lambdaExpr)
    {
        string paramName;
        switch (lambdaExpr)
        {
            case SimpleLambdaExpressionSyntax simple:
                paramName = simple.Parameter.Identifier.Text;
                break;
            case ParenthesizedLambdaExpressionSyntax paren
                when paren.ParameterList.Parameters.Count == 1:
                paramName = paren.ParameterList.Parameters[0].Identifier.Text;
                break;
            default:
                return ImmutableArray<SetBodyWrite>.Empty;
        }

        switch (GetLambdaBody(lambdaExpr))
        {
            case ExpressionSyntax single:
                return TryGetConvertibleWrite(single, paramName) is { } write
                    ? ImmutableArray.Create(write)
                    : ImmutableArray<SetBodyWrite>.Empty;

            case BlockSyntax block:
            {
                var builder = ImmutableArray.CreateBuilder<SetBodyWrite>(block.Statements.Count);
                foreach (var statement in block.Statements)
                {
                    if (statement is not ExpressionStatementSyntax expressionStatement
                        || TryGetConvertibleWrite(expressionStatement.Expression, paramName) is not { } statementWrite)
                    {
                        return ImmutableArray<SetBodyWrite>.Empty;
                    }
                    builder.Add(statementWrite);
                }
                return builder.Count == 0
                    ? ImmutableArray<SetBodyWrite>.Empty
                    : builder.ToImmutable();
            }

            default:
                return ImmutableArray<SetBodyWrite>.Empty;
        }
    }

    private static SetBodyWrite? TryGetConvertibleWrite(ExpressionSyntax expression, string paramName)
    {
        switch (expression)
        {
            case AssignmentExpressionSyntax assignment when IsConvertibleAssignment(assignment, paramName):
                return SetBodyWrite.FromAssignment(assignment);

            case InvocationExpressionSyntax invocation
                when TryMatchAttachedSetterCall(invocation, paramName, out var owner, out var setter, out var value)
                    && !ReferencesIdentifier(value, paramName):
                return SetBodyWrite.FromAttachedCall(invocation, owner + "." + setter, value);

            default:
                return null;
        }
    }

    private static bool IsConvertibleAssignment(AssignmentExpressionSyntax assignment, string paramName)
        // '+=' / '-=' are event subscriptions (REACTOR_EVENT_001's job) and have no modifier form.
        => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && GetAssignedMemberAccess(assignment, paramName) is not null
            && !ReferencesIdentifier(assignment.Right, paramName);

    /// <summary>
    /// True when <paramref name="expression"/> mentions <paramref name="identifier"/> anywhere.
    /// </summary>
    /// <remarks>
    /// The right-hand side is copied verbatim into the modifier call, but the lambda parameter
    /// does not survive the rewrite — the lambda is deleted. So
    /// <c>b.Set(c =&gt; c.IsEnabled = c.Opacity &gt; 0)</c> would become
    /// <c>b.IsEnabled(c.Opacity &gt; 0)</c>, which does not compile. Purely syntactic on
    /// purpose: a shadowing declaration that happens to reuse the name only costs a declined
    /// fix, whereas missing a real reference emits broken code.
    /// </remarks>
    internal static bool ReferencesIdentifier(SyntaxNode expression, string identifier)
        => expression.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(name => string.Equals(name.Identifier.Text, identifier, StringComparison.Ordinal));

    /// <summary>
    /// Extract the single assignment expression from a lambda passed to <c>.Set(...)</c>.
    /// Supports both expression-body lambdas (<c>fe =&gt; fe.X = v</c>) and block-body
    /// lambdas with a single assignment statement (<c>fe =&gt; { fe.X = v; }</c>).
    /// Multi-statement blocks return <c>null</c>: with more than one assignment there is no
    /// single "the" assignment to return. Callers that want to classify a whole body should
    /// use <see cref="GetLambdaAssignments"/>; callers that want to <em>rewrite</em> one —
    /// such as <c>PoolResetSetCodeFix</c>, which turns every statement into a modifier chain
    /// — must use <see cref="GetFullyConvertibleLambdaBody"/>, which additionally proves that
    /// no statement would be dropped by the rewrite.
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
