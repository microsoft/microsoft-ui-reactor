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
/// Code fix for <c>REACTOR_LIFECYCLE_003</c>: switches a <c>Reconciler.UpdateChild</c> call whose
/// result is installed in one slot to <c>Reconciler.Reconcile</c>:
/// <code>
/// var replacement = r.UpdateChild(oldChild, newChild, existing, rerender);
/// if (replacement is not null)
///     slot.Content = replacement;
/// </code>
/// becomes
/// <code>
/// var replacement = r.Reconcile(oldChild, newChild, existing, rerender);
/// if (!ReferenceEquals(replacement, existing))
///     slot.Content = replacement;
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <c>Reconcile</c> takes the same arguments. Where <c>UpdateChild</c> returned null it returns the
/// existing control, unless the new child is empty: then it unmounts the old control and returns
/// null. In every other case it returns the control the slot should hold, having unmounted the one it
/// replaced. So the null check becomes an identity check, which also assigns null, and so empties the
/// slot, when the child is gone, and an <c>UnmountChild(existing)</c> in the body is removed.
/// </para>
/// <para>
/// The fix is only offered where that rewrite keeps the code's meaning:
/// </para>
/// <list type="bullet">
/// <item>The call is <c>r.UpdateChild(...)</c> with four positional arguments. <c>Reconcile</c> names
/// its parameters differently, and after <c>r?.</c> a null receiver would empty the slot.</item>
/// <item>The existing control is a local or parameter that the call doesn't assign, since it is
/// compared with the result afterwards.</item>
/// <item>The result goes into a local read only by the <c>if</c> right after it, whose condition is
/// <c>x is not null</c>, <c>x != null</c> or <c>null != x</c> and which has no <c>else</c>.</item>
/// <item>Apart from <c>UnmountChild(existing)</c> statements, the body is one assignment of the result
/// to a field, property, local or parameter. The target is reached through names and member
/// accesses only, and can hold null.</item>
/// <item>Each <c>UnmountChild(existing)</c> in the block is a statement of its own in the body, with
/// no comment or directive in or around it. It goes through the reconciler the call used (the same
/// local or parameter, or the same handler context's <c>Reconciler</c>). When the result replaces
/// the existing control's own variable, the unmount comes first.</item>
/// </list>
/// <para>
/// Anywhere else the diagnostic stands without a fix.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReconcilerUpdateChildCodeFix))]
[Shared]
public sealed class ReconcilerUpdateChildCodeFix : CodeFixProvider
{
    private const string Title = "Use Reconcile";

    private const string HandlerContextNamespace = "Microsoft.UI.Reactor.Core.V1Protocol";
    private const string ContextReconcilerProperty = "Reconciler";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ReconcilerUpdateChildAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            // The diagnostic sits on the method name of a member access. After r?.UpdateChild(...),
            // the name hangs off a member binding instead, and the fix declines that form.
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not SimpleNameSyntax name)
                continue;
            if (name.Parent is not MemberAccessExpressionSyntax access || access.Name != name)
                continue;
            if (access.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != access)
                continue;

            semanticModel ??= await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null) return;

            var plan = Plan.TryCreate(semanticModel, invocation, access, context.CancellationToken);
            if (plan is null) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(plan.Apply(root))),
                    equivalenceKey: ReconcilerUpdateChildAnalyzer.DiagnosticId),
                diagnostic);
        }
    }

    private sealed class Plan
    {
        private readonly SimpleNameSyntax _name;
        private readonly ExpressionSyntax _condition;
        private readonly ExpressionSyntax _newCondition;
        private readonly ImmutableArray<StatementSyntax> _unmounts;

        private Plan(SimpleNameSyntax name, ExpressionSyntax condition, ExpressionSyntax newCondition,
            ImmutableArray<StatementSyntax> unmounts)
        {
            _name = name;
            _condition = condition;
            _newCondition = newCondition;
            _unmounts = unmounts;
        }

        public SyntaxNode Apply(SyntaxNode root)
        {
            var nodes = new SyntaxNode[] { _name, _condition }.Concat(_unmounts);
            var tracked = root.TrackNodes(nodes);

            tracked = tracked.ReplaceNode(
                tracked.GetCurrentNode(_name)!,
                SyntaxFactory.IdentifierName(ReconcilerUpdateChildAnalyzer.ReconcileName).WithTriviaFrom(_name));
            tracked = tracked.ReplaceNode(
                tracked.GetCurrentNode(_condition)!,
                _newCondition.WithTriviaFrom(_condition));
            if (!_unmounts.IsEmpty)
            {
                tracked = tracked.RemoveNodes(
                    _unmounts.Select(statement => tracked.GetCurrentNode(statement)!),
                    SyntaxRemoveOptions.KeepNoTrivia)!;
            }

            return tracked;
        }

        public static Plan? TryCreate(SemanticModel model, InvocationExpressionSyntax invocation,
            MemberAccessExpressionSyntax access, CancellationToken ct)
        {
            // Reconcile names its parameters differently, so only positional arguments carry over.
            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count != 4)
                return null;
            foreach (var argument in arguments)
            {
                if (argument.NameColon is not null || !argument.RefKindKeyword.IsKind(SyntaxKind.None))
                    return null;
            }

            // var result = r.UpdateChild(...);, directly followed by if (result is not null) ...
            // without an else: an else ran whenever UpdateChild returned null, which includes a
            // child that became empty, and that case now takes the if branch.
            if (invocation.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
                || declarator.Parent is not VariableDeclarationSyntax { Variables.Count: 1 } declaration
                || declaration.Parent is not LocalDeclarationStatementSyntax statement
                || !statement.UsingKeyword.IsKind(SyntaxKind.None)
                || statement.Parent is not BlockSyntax block)
                return null;
            if (model.GetDeclaredSymbol(declarator, ct) is not ILocalSymbol result)
                return null;

            var index = block.Statements.IndexOf(statement);
            if (index + 1 >= block.Statements.Count || block.Statements[index + 1] is not IfStatementSyntax ifStatement)
                return null;
            if (ifStatement.Else is not null || !IsNotNullCheckOf(model, ifStatement.Condition, result, ct))
                return null;

            // The existing control is compared with the result after the call, so it has to be a
            // local or parameter that the call doesn't assign: re-reading anything else could see
            // another value.
            var callFlow = model.AnalyzeDataFlow(statement);
            if (callFlow is null || !callFlow.Succeeded)
                return null;
            if (arguments[2].Expression is not IdentifierNameSyntax existing)
                return null;
            var existingSymbol = model.GetSymbolInfo(existing, ct).Symbol;
            if (existingSymbol is null || !IsLocalTheCallLeaves(existingSymbol, callFlow))
                return null;

            // Reconcile returns the existing control where UpdateChild returned null, so the result
            // may be read only where the rewritten condition has already told the two apart.
            var resultReads = block.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(reference => reference.Identifier.ValueText == result.Name
                    && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(reference, ct).Symbol, result));
            if (resultReads.Any(reference => !ifStatement.Condition.Span.Contains(reference.Span)
                    && !ifStatement.Statement.Span.Contains(reference.Span)))
                return null;

            // Reconcile unmounts the control it replaces, so an UnmountChild of it in the body would
            // run a second time. Remove it where it is a statement of its own in the body's block;
            // anywhere else in the block, decline.
            var body = ifStatement.Statement as BlockSyntax;
            var callReceiver = ReceiverOf(model, access.Expression, callFlow, ct);
            var unmounts = ImmutableArray.CreateBuilder<StatementSyntax>();
            foreach (var call in block.DescendantNodes().OfType<InvocationExpressionSyntax>()
                         .Where(call => IsUnmountOf(model, call, existingSymbol, ct)))
            {
                if (body is null || call.Parent is not ExpressionStatementSyntax unmount || unmount.Parent != body)
                    return null;
                // Removing the statement removes the trivia in and around it, so that has to be
                // layout only: a comment would be lost, and an #endif before it would unbalance the file.
                if (!IsLayoutOnly(unmount))
                    return null;
                // Each reconciler keeps its own component and handler state, and Reconcile unmounts
                // through the reconciler the call used, so only an unmount through that one goes.
                // The body can't reassign it: what stays in it is one assignment of a control.
                var unmountReceiver = ReceiverOf(model, ((MemberAccessExpressionSyntax)call.Expression).Expression, callFlow, ct);
                if (callReceiver.IsDefault || unmountReceiver.IsDefault
                    || !callReceiver.SequenceEqual(unmountReceiver, SymbolEqualityComparer.Default))
                    return null;
                unmounts.Add(unmount);
            }

            // What remains of the body installs the result. Without the null check it also runs when
            // the child is gone, so it has to be an assignment that empties the slot when given null.
            var remaining = (body is null ? new[] { ifStatement.Statement } : body.Statements.AsEnumerable())
                .Where(bodyStatement => !unmounts.Contains(bodyStatement))
                .ToList();
            if (remaining.Count != 1
                || remaining[0] is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } install
                || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                || assignment.Right is not IdentifierNameSyntax assigned
                || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assigned, ct).Symbol, result)
                || !IsClearableTarget(model, assignment.Left, result, ct))
                return null;
            // When the result replaces the existing control's own variable, an unmount after that
            // assignment received the result, not the control it replaced.
            if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left, ct).Symbol, existingSymbol)
                && unmounts.Any(unmount => unmount.SpanStart > install.SpanStart))
                return null;

            ExpressionSyntax referenceEquals = SyntaxFactory.IdentifierName(nameof(object.ReferenceEquals));
            if (!BindsToObjectReferenceEquals(model, ifStatement.Condition.SpanStart))
            {
                referenceEquals = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)),
                    (SimpleNameSyntax)referenceEquals);
            }
            var newCondition = SyntaxFactory.PrefixUnaryExpression(
                    SyntaxKind.LogicalNotExpression,
                    SyntaxFactory.InvocationExpression(
                        referenceEquals,
                        SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
                        {
                            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(declarator.Identifier.WithoutTrivia())),
                            SyntaxFactory.Argument(existing.WithoutTrivia()),
                        }))))
                .NormalizeWhitespace();

            return new Plan(access.Name, ifStatement.Condition, newCondition, unmounts.ToImmutable());
        }

        /// <summary><c>x is not null</c>, <c>x != null</c> or <c>null != x</c> on <paramref name="local"/>.</summary>
        private static bool IsNotNullCheckOf(SemanticModel model, ExpressionSyntax condition, ILocalSymbol local, CancellationToken ct)
        {
            ExpressionSyntax? operand = null;
            if (condition is IsPatternExpressionSyntax isPattern
                && isPattern.Pattern is UnaryPatternSyntax unary
                && unary.OperatorToken.IsKind(SyntaxKind.NotKeyword)
                && unary.Pattern is ConstantPatternSyntax constant
                && constant.Expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                operand = isPattern.Expression;
            }
            else if (condition is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.NotEqualsExpression))
            {
                if (binary.Right.IsKind(SyntaxKind.NullLiteralExpression))
                    operand = binary.Left;
                else if (binary.Left.IsKind(SyntaxKind.NullLiteralExpression))
                    operand = binary.Right;
            }

            return operand is IdentifierNameSyntax identifier
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, ct).Symbol, local);
        }

        /// <summary>A <c>Reconciler.UnmountChild</c> call on <paramref name="control"/>.</summary>
        private static bool IsUnmountOf(SemanticModel model, InvocationExpressionSyntax call, ISymbol control, CancellationToken ct)
        {
            if (call.Expression is not MemberAccessExpressionSyntax access
                || access.Name.Identifier.ValueText != ReconcilerUpdateChildAnalyzer.UnmountChildName
                || call.ArgumentList.Arguments.Count != 1)
                return false;
            if (model.GetSymbolInfo(call, ct).Symbol is not IMethodSymbol method
                || !ReconcilerUpdateChildAnalyzer.IsReconcilerMethod(method, ReconcilerUpdateChildAnalyzer.UnmountChildName))
                return false;
            return call.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax argument
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(argument, ct).Symbol, control);
        }

        /// <summary>
        /// The symbols that name the reconciler <paramref name="receiver"/> evaluates to, when they
        /// pin down one instance for the whole block: a local or parameter that the call doesn't
        /// assign, or the <c>Reconciler</c> of such a local's or parameter's handler context.
        /// Otherwise default.
        /// </summary>
        private static ImmutableArray<ISymbol> ReceiverOf(SemanticModel model, ExpressionSyntax receiver,
            DataFlowAnalysis callFlow, CancellationToken ct)
        {
            switch (receiver)
            {
                case IdentifierNameSyntax name
                    when model.GetSymbolInfo(name, ct).Symbol is { } symbol && IsLocalTheCallLeaves(symbol, callFlow):
                    return ImmutableArray.Create(symbol);
                case MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax owner } member
                    when member.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                        && model.GetSymbolInfo(owner, ct).Symbol is { } context
                        && IsLocalTheCallLeaves(context, callFlow)
                        && model.GetSymbolInfo(member, ct).Symbol is IPropertySymbol { Name: ContextReconcilerProperty } property
                        && IsHandlerContext(property.ContainingType):
                    return ImmutableArray.Create(context, property);
                default:
                    return default;
            }
        }

        /// <summary><c>MountContext</c>, <c>UpdateContext</c> or <c>UnmountContext</c>: each holds one reconciler in a readonly field.</summary>
        private static bool IsHandlerContext(INamedTypeSymbol type) =>
            type.Name is "MountContext" or "UpdateContext" or "UnmountContext"
            && type.ContainingNamespace?.ToDisplayString() == HandlerContextNamespace;

        /// <summary>A local or parameter that the <c>UpdateChild</c> statement doesn't assign.</summary>
        private static bool IsLocalTheCallLeaves(ISymbol symbol, DataFlowAnalysis callFlow) =>
            symbol is (ILocalSymbol or IParameterSymbol) && !IsWritten(callFlow, symbol);

        private static bool IsWritten(DataFlowAnalysis flow, ISymbol symbol) =>
            flow.WrittenInside.Any(written => SymbolEqualityComparer.Default.Equals(written, symbol));

        /// <summary>
        /// True when <paramref name="target"/> is a field, property, local or parameter reached
        /// through names and member accesses, not through <paramref name="result"/>, that can be
        /// assigned null without a nullable warning.
        /// </summary>
        private static bool IsClearableTarget(SemanticModel model, ExpressionSyntax target, ILocalSymbol result,
            CancellationToken ct)
        {
            var node = target;
            while (node is MemberAccessExpressionSyntax member && member.IsKind(SyntaxKind.SimpleMemberAccessExpression))
                node = member.Expression;
            if (node is IdentifierNameSyntax rootName)
            {
                if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(rootName, ct).Symbol, result))
                    return false;
            }
            else if (node is not ThisExpressionSyntax)
            {
                return false;
            }

            var type = model.GetSymbolInfo(target, ct).Symbol switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null,
            };
            if (type is null)
                return false;
            return !(type.IsReferenceType
                && type.NullableAnnotation == NullableAnnotation.NotAnnotated
                && model.GetNullableContext(target.SpanStart).WarningsEnabled());
        }

        /// <summary>True when the only trivia in and around <paramref name="statement"/> is whitespace and line breaks.</summary>
        private static bool IsLayoutOnly(StatementSyntax statement)
        {
            foreach (var trivia in statement.DescendantTrivia())
            {
                if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// True when a bare <c>ReferenceEquals</c> at <paramref name="position"/> binds to
        /// <c>object.ReferenceEquals</c>; a member of the same name on the containing type hides it.
        /// </summary>
        private static bool BindsToObjectReferenceEquals(SemanticModel model, int position)
        {
            var probe = SyntaxFactory.ParseExpression("ReferenceEquals(null, null)");
            var symbol = model.GetSpeculativeSymbolInfo(position, probe, SpeculativeBindingOption.BindAsExpression).Symbol;
            return symbol is IMethodSymbol { Name: "ReferenceEquals" } method
                && method.ContainingType?.SpecialType == SpecialType.System_Object;
        }
    }
}
