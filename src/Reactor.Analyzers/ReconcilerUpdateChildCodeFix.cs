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
/// Code fix for <c>REACTOR_LIFECYCLE_003</c>: switches a <c>Reconciler.UpdateChild</c> call to
/// <c>Reconciler.Reconcile</c> in the shape both of the repository's call sites had:
/// <code>
/// var replacement = r.UpdateChild(oldChild, newChild, existing, rerender);
/// if (replacement is not null)
///     slot = replacement;
/// </code>
/// becomes
/// <code>
/// var replacement = r.Reconcile(oldChild, newChild, existing, rerender);
/// if (replacement is not null &amp;&amp; !ReferenceEquals(replacement, existing))
///     slot = replacement;
/// </code>
/// </summary>
/// <remarks>
/// <c>Reconcile</c> takes the same arguments but returns the existing control where
/// <c>UpdateChild</c> returned null, so the null check gains an identity check, and it unmounts a
/// control it replaces, so an <c>UnmountChild(existing)</c> statement in the body is removed. The
/// null check stays: <c>Reconcile</c> returns null for an empty new child, and the body was written
/// for a control. The fix is only offered where that rewrite provably keeps the meaning: the result
/// is read nowhere but that <c>if</c>, the existing control is a plain local or parameter (it is
/// compared with the result), the arguments are positional (<c>Reconcile</c> names its parameters
/// differently), and every <c>UnmountChild(existing)</c> in the block is a statement of its own in
/// the body. Anywhere else the diagnostic stands without a fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReconcilerUpdateChildCodeFix))]
[Shared]
public sealed class ReconcilerUpdateChildCodeFix : CodeFixProvider
{
    private const string Title = "Use Reconcile";

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
            // The diagnostic sits on the method name of a member access.
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not SimpleNameSyntax name)
                continue;
            if (name.Parent is not MemberAccessExpressionSyntax access || access.Name != name)
                continue;
            if (access.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != access)
                continue;

            semanticModel ??= await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null) return;

            var plan = Plan.TryCreate(semanticModel, invocation, context.CancellationToken);
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

        public static Plan? TryCreate(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
        {
            var access = (MemberAccessExpressionSyntax)invocation.Expression;

            // Reconcile names its parameters differently, so only positional arguments carry over.
            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count != 4)
                return null;
            foreach (var argument in arguments)
            {
                if (argument.NameColon is not null || !argument.RefKindKeyword.IsKind(SyntaxKind.None))
                    return null;
            }

            // The existing control is compared with Reconcile's result after the call, so it has to
            // be a plain local or parameter: re-reading anything else could see another value.
            if (arguments[2].Expression is not IdentifierNameSyntax existing)
                return null;
            var existingSymbol = model.GetSymbolInfo(existing, ct).Symbol;
            if (existingSymbol is not (ILocalSymbol or IParameterSymbol))
                return null;

            // var result = r.UpdateChild(...);, directly followed by if (result is not null) ...
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
            if (!IsNotNullCheckOf(model, ifStatement.Condition, result, ct))
                return null;

            // Reconcile returns the existing control where UpdateChild returned null, so the result
            // may be read only where the rewritten condition has already told the two apart.
            foreach (var reference in block.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (reference.Identifier.ValueText != result.Name)
                    continue;
                if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(reference, ct).Symbol, result))
                    continue;
                if (!ifStatement.Condition.Span.Contains(reference.Span)
                    && !ifStatement.Statement.Span.Contains(reference.Span))
                    return null;
            }

            // Reconcile unmounts the control it replaces, so an UnmountChild of it in the body would
            // run a second time. Remove it where it is a statement of its own in the body's block;
            // anywhere else in the block, decline.
            var unmounts = ImmutableArray.CreateBuilder<StatementSyntax>();
            foreach (var call in block.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!IsUnmountOf(model, call, existingSymbol, ct))
                    continue;
                if (call.Parent is not ExpressionStatementSyntax unmount
                    || ifStatement.Statement is not BlockSyntax body
                    || unmount.Parent != body)
                    return null;
                unmounts.Add(unmount);
            }
            if (unmounts.Count > 0 && ((BlockSyntax)ifStatement.Statement).Statements.Count == unmounts.Count)
                return null;

            var referenceEquals = BindsToObjectReferenceEquals(model, ifStatement.Condition.SpanStart)
                ? "ReferenceEquals"
                : "object.ReferenceEquals";
            var newCondition = SyntaxFactory.ParseExpression(
                $"{ifStatement.Condition.WithoutTrivia()} && !{referenceEquals}({declarator.Identifier.Text}, {existing.Identifier.Text})");

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
