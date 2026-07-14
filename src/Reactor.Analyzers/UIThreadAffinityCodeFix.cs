using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for REACTOR_THREAD_001: marshals a UI-thread-only call that runs on a
/// background thread back onto the UI thread through the Reactor dispatcher.
/// </summary>
/// <remarks>
/// The rewrite is null-safe by design — <c>ReactorApp.UIDispatcher</c> is a
/// <c>DispatcherQueue?</c> that is null until the first window bootstraps, so the
/// fix falls back to the direct call rather than a null-forgiving <c>!</c>:
/// <code>
/// var d = ReactorApp.UIDispatcher;
/// if (d is null)
///     window.Close();
/// else
///     d.TryEnqueue(() =&gt; window.Close());
/// </code>
/// It handles the two common shapes — the flagged call as a statement inside a
/// background lambda block, and the flagged call as the expression body of the
/// background lambda. Other shapes leave the warning unfixed (the trap is still
/// reported); a non-void expression-bodied lambda is skipped because turning it
/// into a block would drop the produced value.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UIThreadAffinityCodeFix))]
[Shared]
public sealed class UIThreadAffinityCodeFix : CodeFixProvider
{
    private const string Title = "Marshal call onto the UI thread via ReactorApp.UIDispatcher";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UIThreadAffinityAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

            // The flagged node is a UI-thread-only call, a property-set assignment,
            // or an increment/decrement; all are marshaled the same way. Identify it
            // by its exact reported span.
            var core = node.AncestorsAndSelf()
                .FirstOrDefault(n => n is InvocationExpressionSyntax or AssignmentExpressionSyntax
                        or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax
                    && n.Span == diagnostic.Location.SourceSpan) as ExpressionSyntax;
            if (core is null) continue;

            // Shape A: the call/assignment is a stand-alone statement — always safe
            // to wrap, since the enclosing lambda/method body is already statement-bodied.
            if (core.Parent is ExpressionStatementSyntax statement)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        ct => Task.FromResult(FixStatement(context.Document, root, statement, core)),
                        equivalenceKey: UIThreadAffinityAnalyzer.DiagnosticId),
                    diagnostic);
                continue;
            }

            // Shape B: it is the expression body of the background lambda
            // (`Task.Run(() => window.Close())`). Turning the expression body into a
            // block only preserves semantics when the lambda is bound to a
            // void-returning delegate (Action/Action<T>) — otherwise a value-producing
            // body (a call with a result, an assignment, or an increment) may be bound
            // to a Func<T> overload whose result is consumed, and the rewrite would
            // change overload resolution / return type. Marshaling is fire-and-forget,
            // so a produced value cannot be carried across the dispatcher anyway.
            if (core.Parent is LambdaExpressionSyntax lambda && lambda.ExpressionBody == core)
            {
                var semanticModel = await context.Document
                    .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
                if (semanticModel is null) continue;
                if (semanticModel.GetTypeInfo(lambda, context.CancellationToken).ConvertedType
                        is not INamedTypeSymbol { DelegateInvokeMethod.ReturnsVoid: true })
                    continue;

                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        ct => Task.FromResult(FixExpressionLambda(context.Document, root, lambda, core)),
                        equivalenceKey: UIThreadAffinityAnalyzer.DiagnosticId),
                    diagnostic);
            }
        }
    }

    private static Document FixStatement(
        Document document,
        SyntaxNode root,
        ExpressionStatementSyntax statement,
        ExpressionSyntax core)
    {
        var name = PickDispatcherName(core);
        var declaration = BuildDispatcherDeclaration(name);
        var dispatchIf = BuildDispatchIf(core, name);

        SyntaxNode newRoot;
        if (statement.Parent is BlockSyntax block)
        {
            // Insert the declaration + guard in place of the flagged statement,
            // leaving the block's other statements (and their comments/directives)
            // untouched. Preserve the flagged statement's own leading trivia so a
            // comment above it survives; format only the inserted nodes.
            var index = block.Statements.IndexOf(statement);
            var declarationStmt = declaration
                .NormalizeWhitespace(elasticTrivia: true)
                .WithLeadingTrivia(statement.GetLeadingTrivia())
                .WithAdditionalAnnotations(Formatter.Annotation);
            var guardStmt = dispatchIf
                .NormalizeWhitespace(elasticTrivia: true)
                .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
                .WithTrailingTrivia(statement.GetTrailingTrivia())
                .WithAdditionalAnnotations(Formatter.Annotation);
            var newStatements = block.Statements
                .RemoveAt(index)
                .Insert(index, guardStmt)
                .Insert(index, declarationStmt);
            newRoot = root.ReplaceNode(block, block.WithStatements(newStatements));
        }
        else
        {
            // Embedded (braceless) or switch-section context — wrap in a block so
            // the two replacement statements stay well-formed, carrying the
            // statement's trivia onto the wrapper.
            var wrapper = SyntaxFactory.Block(declaration, dispatchIf)
                .NormalizeWhitespace(elasticTrivia: true)
                .WithTriviaFrom(statement)
                .WithAdditionalAnnotations(Formatter.Annotation);
            newRoot = root.ReplaceNode(statement, wrapper);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static Document FixExpressionLambda(
        Document document,
        SyntaxNode root,
        LambdaExpressionSyntax lambda,
        ExpressionSyntax core)
    {
        var name = PickDispatcherName(core);
        var block = SyntaxFactory.Block(BuildDispatcherDeclaration(name), BuildDispatchIf(core, name));

        LambdaExpressionSyntax newLambda = lambda switch
        {
            SimpleLambdaExpressionSyntax simple =>
                simple.WithExpressionBody(null).WithBlock(block),
            ParenthesizedLambdaExpressionSyntax paren =>
                paren.WithExpressionBody(null).WithBlock(block),
            _ => lambda,
        };

        newLambda = newLambda
            .NormalizeWhitespace(elasticTrivia: true)
            .WithTriviaFrom(lambda)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return document.WithSyntaxRoot(root.ReplaceNode(lambda, newLambda));
    }

    /// <summary>
    /// Pick a local name for the dispatcher that does not collide with any
    /// identifier already used in the enclosing member — prefers <c>d</c> (matching
    /// the documented idiom), falling back to <c>dispatcher</c>, <c>dispatcher2</c>, …
    /// </summary>
    private static string PickDispatcherName(SyntaxNode core)
    {
        var scope = core.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        if (scope is not null)
        {
            foreach (var token in scope.DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.IdentifierToken))
                    used.Add(token.ValueText);
            }
        }

        if (!used.Contains("d")) return "d";
        for (var i = 1; ; i++)
        {
            var candidate = i == 1 ? "dispatcher" : "dispatcher" + i.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }

    /// <summary>Builds <c>var &lt;name&gt; = ReactorApp.UIDispatcher;</c>.</summary>
    private static LocalDeclarationStatementSyntax BuildDispatcherDeclaration(string name)
    {
        var dispatcherAccess = SyntaxFactory
            .ParseExpression("global::Microsoft.UI.Reactor.ReactorApp.UIDispatcher")
            .WithAdditionalAnnotations(Simplifier.Annotation);

        return SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name))
                        .WithInitializer(SyntaxFactory.EqualsValueClause(dispatcherAccess)))))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    /// <summary>
    /// Builds <c>if (&lt;name&gt; is null) &lt;core&gt;; else &lt;name&gt;.TryEnqueue(() =&gt; &lt;core&gt;);</c>
    /// where <c>&lt;core&gt;</c> is the flagged call or property-set assignment.
    /// </summary>
    private static IfStatementSyntax BuildDispatchIf(ExpressionSyntax core, string name)
    {
        var condition = SyntaxFactory.IsPatternExpression(
            SyntaxFactory.IdentifierName(name),
            SyntaxFactory.ConstantPattern(
                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)));

        var directCall = SyntaxFactory.ExpressionStatement((ExpressionSyntax)core.WithoutTrivia());

        var marshaledCall = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(name),
                    SyntaxFactory.IdentifierName("TryEnqueue")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.ParenthesizedLambdaExpression()
                                .WithExpressionBody((ExpressionSyntax)core.WithoutTrivia()))))));

        return SyntaxFactory.IfStatement(condition, directCall)
            .WithElse(SyntaxFactory.ElseClause(marshaledCall))
            .WithAdditionalAnnotations(Formatter.Annotation);
    }
}
