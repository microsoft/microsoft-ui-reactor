using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for <see cref="MissingWithKeyAnalyzer"/> (<c>REACTOR_DSL_001</c>) —
/// appends <c>.WithKey(...)</c> to the lambda body of a <c>.Select(item => …)</c>
/// projection whose result is consumed by a layout factory.
///
/// Three offers, in order of preference:
/// <list type="number">
/// <item><description><c>.WithKey(item)</c> when <c>T</c> implements
///   <c>IReactorKeyed</c> (spec 042 §5 — identity on data).</description></item>
/// <item><description><c>.WithKey(item.Key)</c> when <c>T</c> has a public
///   property named <c>Key</c>.</description></item>
/// <item><description><c>.WithKey(item.Id)</c> when <c>T</c> has a public
///   property named <c>Id</c>.</description></item>
/// </list>
///
/// Falls back to no fix when none of the above are discoverable — the
/// developer has to decide what the stable identity actually is.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingWithKeyCodeFix))]
[Shared]
public sealed class MissingWithKeyCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(MissingWithKeyAnalyzer.Id);

    // BatchFixer is not safe here: each lambda body needs an independent semantic
    // lookup of the parameter's type, and the candidate property names
    // (`Id` / `Key`) only resolve in the per-document semantic model. Opting
    // out keeps the IDE from offering "Fix all in solution" with the wrong key.
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var selectInv = root.FindNode(diagnostic.Location.SourceSpan) as InvocationExpressionSyntax;
            if (selectInv is null) continue;

            // The analyzer only fires when:
            //   .Select(lambda) — single argument, lambda body is an Invocation
            // so we can re-derive the same shape without re-running the heuristic.
            if (selectInv.ArgumentList.Arguments.Count != 1) continue;
            if (selectInv.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda) continue;

            var body = lambda.Body;
            ExpressionSyntax? targetExpr = null;
            if (body is InvocationExpressionSyntax inv)
            {
                targetExpr = inv;
            }
            else if (body is BlockSyntax block
                     && block.Statements.Count == 1
                     && block.Statements[0] is ReturnStatementSyntax ret
                     && ret.Expression is InvocationExpressionSyntax invRet)
            {
                targetExpr = invRet;
            }
            if (targetExpr is null) continue;

            // Resolve the lambda parameter symbol and its type.
            var paramName = ExtractParameterName(lambda);
            if (paramName is null) continue;

            ITypeSymbol? paramType = ResolveParameterType(model, lambda, paramName, context.CancellationToken);

            // Offer each candidate that's actually reachable on the type. When
            // the parameter is `var` / unbound (no semantic info), still offer
            // the IReactorKeyed and Id/Key forms guarded by a string lookup —
            // the user will get a compile error if the property doesn't exist,
            // but the codefix shouldn't suppress itself just because we lost
            // the semantic model (rare in IDE, common in single-file scripts).

            if (paramType is not null && ImplementsIReactorKeyed(paramType))
            {
                Register(context, diagnostic, root, targetExpr,
                    keyExpression: SyntaxFactory.IdentifierName(paramName),
                    title: $"Add .WithKey({paramName}) — {paramName} : IReactorKeyed",
                    equivalence: "WithKey_Item");
            }

            if (paramType is null || HasPublicProperty(paramType, "Key"))
            {
                Register(context, diagnostic, root, targetExpr,
                    keyExpression: SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(paramName),
                        SyntaxFactory.IdentifierName("Key")),
                    title: $"Add .WithKey({paramName}.Key)",
                    equivalence: "WithKey_Item_Key");
            }

            if (paramType is null || HasPublicProperty(paramType, "Id"))
            {
                Register(context, diagnostic, root, targetExpr,
                    keyExpression: SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(paramName),
                        SyntaxFactory.IdentifierName("Id")),
                    title: $"Add .WithKey({paramName}.Id)",
                    equivalence: "WithKey_Item_Id");
            }
        }
    }

    static string? ExtractParameterName(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
        ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count == 1
            => paren.ParameterList.Parameters[0].Identifier.ValueText,
        _ => null,
    };

    static ITypeSymbol? ResolveParameterType(
        SemanticModel model,
        LambdaExpressionSyntax lambda,
        string paramName,
        System.Threading.CancellationToken ct)
    {
        // GetSymbolInfo on the lambda yields the IMethodSymbol with parameters.
        var symInfo = model.GetSymbolInfo(lambda, ct);
        if (symInfo.Symbol is IMethodSymbol method && method.Parameters.Length >= 1)
            return method.Parameters[0].Type;

        // Fallback: declared parameter syntax with an explicit type.
        var paramSyntax = lambda switch
        {
            SimpleLambdaExpressionSyntax s => s.Parameter,
            ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters[0],
            _ => null,
        };
        if (paramSyntax?.Type is { } typeSyntax)
        {
            var typeInfo = model.GetTypeInfo(typeSyntax, ct);
            return typeInfo.Type;
        }

        return null;
    }

    static bool ImplementsIReactorKeyed(ITypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            // Match by name + containing namespace to avoid pulling a hard
            // reference to Reactor.Core into the analyzer assembly.
            if (iface.Name == "IReactorKeyed"
                && iface.ContainingNamespace?.ToDisplayString() == "Microsoft.UI.Reactor.Core")
                return true;
        }
        return false;
    }

    static bool HasPublicProperty(ITypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol prop && prop.DeclaredAccessibility == Accessibility.Public)
                    return true;
            }
        }
        return false;
    }

    static void Register(
        CodeFixContext context,
        Diagnostic diagnostic,
        SyntaxNode root,
        ExpressionSyntax targetExpr,
        ExpressionSyntax keyExpression,
        string title,
        string equivalence)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                ct =>
                {
                    var wrapped = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            targetExpr.WithoutTrivia(),
                            SyntaxFactory.IdentifierName("WithKey")),
                        SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(keyExpression))))
                        .WithTriviaFrom(targetExpr);

                    var newRoot = root.ReplaceNode(targetExpr, wrapped);
                    return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                },
                equivalenceKey: $"{MissingWithKeyAnalyzer.Id}_{equivalence}"),
            diagnostic);
    }
}
