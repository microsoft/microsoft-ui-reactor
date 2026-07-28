using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for REACTOR_EVENT_001. Offers up to two render-safe rewrites of
/// <c>x.Set(c =&gt; c.Event += h)</c>:
/// <list type="bullet">
/// <item>the declarative <c>.On{Event}(h)</c> modifier, when Reactor exposes one for the
/// event (see <see cref="SetEventSubscriptionAnalyzer.EventModifiers"/>) — safe for any
/// handler because the modifier owns the subscription lifecycle; and</item>
/// <item><c>.OnMountAdd(c =&gt; ((TControl)c).Event += h).OnUnmountAdd(c =&gt; ((TControl)c).Event -= h)</c>,
/// offered only when the handler <c>h</c> is a stable delegate (a <c>static</c> method group
/// or a field) so the mount <c>+=</c> and unmount <c>-=</c> reference the same delegate.</item>
/// </list>
/// Only the subscribe (<c>+=</c>) shape is rewritten; <c>-=</c> is nudge-only.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SetEventSubscriptionCodeFix))]
[Shared]
public sealed class SetEventSubscriptionCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(SetEventSubscriptionAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            if (node is not InvocationExpressionSyntax invocation)
                continue;
            if (!SetLambdaHelpers.IsSetInvocation(invocation, out var memberAccess))
                continue;

            var lambdaExpr = invocation.ArgumentList.Arguments[0].Expression;
            var assignment = SetLambdaHelpers.TryGetLambdaAssignment(lambdaExpr);
            if (assignment is null || !assignment.IsKind(SyntaxKind.AddAssignmentExpression))
                continue; // Only the subscribe (+=) case has a mechanical rewrite.

            var lambdaParam = SetLambdaHelpers.GetSingleLambdaParameter(lambdaExpr);
            if (lambdaParam is null)
                continue;
            var leftAccess = SetLambdaHelpers.GetAssignedMemberAccess(assignment, lambdaParam.Identifier.Text);
            if (leftAccess is null)
                continue;

            var eventName = leftAccess.Name.Identifier.Text;
            var handler = assignment.Right;

            // Fix A — declarative modifier, when Reactor exposes one for this event. The
            // native event uses a distinct delegate type while the modifier takes
            // Action<object, TArgs>; a method group or lambda converts to both, but a
            // delegate-typed *value* (field/local/property/parameter, or `new D(...)`) does
            // not, so the modifier rewrite is only offered for convertible handler shapes.
            if (SetEventSubscriptionAnalyzer.EventModifiers.TryGetValue(eventName, out var modifierName)
                && IsModifierFixCompatible(handler, model, context.CancellationToken))
            {
                var modifierInvocation = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            memberAccess.Expression,
                            SyntaxFactory.IdentifierName(modifierName)),
                        SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(handler))))
                    .WithTriviaFrom(invocation);

                context.RegisterCodeFix(
                    CodeAction.Create(
                        $"Use .{modifierName}(...) modifier",
                        ct => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(invocation, modifierInvocation))),
                        equivalenceKey: SetEventSubscriptionAnalyzer.DiagnosticId + ":modifier:" + modifierName),
                    diagnostic);
            }

            // Fix B — .OnMountAdd/.OnUnmountAdd, when the handler is a stable delegate.
            if (IsStableHandler(handler, model, context.CancellationToken))
            {
                var controlType = model.GetDeclaredSymbol(lambdaParam, context.CancellationToken)?.Type;
                if (controlType is null)
                    continue;

                var paramName = lambdaParam.Identifier.Text;
                var controlName = controlType.ToMinimalDisplayString(model, invocation.SpanStart);
                var receiverText = memberAccess.Expression.ToString();
                var handlerText = handler.ToString();

                var replacementText =
                    $"{receiverText}.OnMountAdd({paramName} => (({controlName}){paramName}).{eventName} += {handlerText})" +
                    $".OnUnmountAdd({paramName} => (({controlName}){paramName}).{eventName} -= {handlerText})";

                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Move event subscription to .OnMountAdd/.OnUnmountAdd",
                        ct =>
                        {
                            var replacement = SyntaxFactory.ParseExpression(replacementText)
                                .WithTriviaFrom(invocation);
                            return Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(invocation, replacement)));
                        },
                        equivalenceKey: SetEventSubscriptionAnalyzer.DiagnosticId + ":mount"),
                    diagnostic);
            }
        }
    }

    /// <summary>
    /// Whether the declarative <c>.On*</c> modifier rewrite (Fix A) is type-safe for this
    /// handler. The modifier parameter is <c>Action&lt;object, TArgs&gt;</c>; the native event
    /// uses a distinct delegate type. A method group and an anonymous function undergo delegate
    /// <em>conversion</em> to the <c>Action</c>, but a delegate-typed value (field / local /
    /// property / parameter) or an explicit <c>new SomeDelegate(...)</c> has no such conversion,
    /// so rewriting <c>.On{Event}(value)</c> would not compile (CS1503). Offer Fix A only for
    /// the convertible shapes.
    /// </summary>
    private static bool IsModifierFixCompatible(ExpressionSyntax handler, SemanticModel model, CancellationToken ct)
    {
        if (handler is AnonymousFunctionExpressionSyntax)
            return true;
        if (handler is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
            return false;

        var info = model.GetSymbolInfo(handler, ct);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();

        // A method group resolves to the (non-constructor) method; a delegate-typed value
        // resolves to a field/local/property/parameter (or a constructor for `new D(...)`).
        return symbol is IMethodSymbol { MethodKind: not MethodKind.Constructor };
    }

    /// <summary>
    /// A handler is stable across renders — safe to <c>+=</c> at mount and <c>-=</c> at
    /// unmount — when it is a <c>static</c> (ordinary) method group or a field reference.
    /// Lambdas, anonymous methods, locals, and <b>properties</b> are treated as unstable: a
    /// property getter may return a fresh delegate on each call, so the mount <c>+=</c> and
    /// unmount <c>-=</c> could reference different delegates and leak.
    /// </summary>
    private static bool IsStableHandler(ExpressionSyntax handler, SemanticModel model, CancellationToken ct)
    {
        if (handler is AnonymousFunctionExpressionSyntax)
            return false;

        var info = model.GetSymbolInfo(handler, ct);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();

        return symbol switch
        {
            IMethodSymbol method => method.IsStatic && method.MethodKind == MethodKind.Ordinary,
            IFieldSymbol => true,
            _ => false,
        };
    }
}
