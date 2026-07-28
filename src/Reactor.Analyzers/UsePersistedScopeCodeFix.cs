using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for <see cref="UsePersistedScopeAnalyzer"/> (<c>REACTOR_PERSIST_001</c>).
/// Appends an explicit scope argument to a two-argument <c>UsePersisted(key, initial)</c>
/// call, offering two actions:
/// <list type="bullet">
///   <item><c>, PersistedScope.Window</c> — host-lifetime scope (recommended).</item>
///   <item><c>, PersistedScope.Application</c> — process-wide, i.e. make the current
///   implicit behavior explicit.</item>
/// </list>
/// </summary>
/// <remarks>
/// Both actions are always safe (the three-argument overload always exists), so both
/// are offered unconditionally. Two robustness details keep the rewrite compiling:
/// the <c>PersistedScope</c> reference is rendered with
/// <see cref="SymbolDisplayExtensions.ToMinimalDisplayString"/> so it is qualified
/// exactly as much as the call site needs (bare <c>PersistedScope</c> when the
/// namespace is imported, otherwise fully qualified); and the appended argument is
/// passed by name (<c>scope:</c>) whenever the original call already uses named
/// arguments, so a call with reordered named arguments does not become an illegal
/// "positional after named" argument list.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UsePersistedScopeCodeFix))]
[Shared]
public sealed class UsePersistedScopeCodeFix : CodeFixProvider
{
    private const string ScopeTypeName = "PersistedScope";
    private const string ScopeTypeMetadataName = "Microsoft.UI.Reactor.Core.PersistedScope";
    private const string ScopeTypeFullyQualifiedName = "global::Microsoft.UI.Reactor.Core.PersistedScope";
    private const string ScopeParameterName = "scope";
    private const string RecommendedScope = "Window";
    private const string ExplicitScope = "Application";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(UsePersistedScopeAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var invocation = node as InvocationExpressionSyntax
                ?? node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation is null) continue;
            if (invocation.ArgumentList.Arguments.Count != 2) continue;

            semanticModel ??= await context.Document
                .GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

            // Shortest name for PersistedScope that compiles at this call site — bare
            // `PersistedScope` when the namespace is imported (the common case),
            // otherwise a qualified name. Falls back to the fully-qualified global
            // name if the symbol can't be resolved (e.g. an ambiguous metadata lookup),
            // so the emitted fix compiles even without a `using` at the call site.
            var scopeTypeName = semanticModel?.Compilation
                .GetTypeByMetadataName(ScopeTypeMetadataName)
                ?.ToMinimalDisplayString(semanticModel, invocation.SpanStart)
                ?? ScopeTypeFullyQualifiedName;

            var useNamedArgument = invocation.ArgumentList.Arguments.Any(static a => a.NameColon is not null);

            RegisterScopeFix(context, root, invocation, diagnostic, RecommendedScope, scopeTypeName, useNamedArgument,
                $"Scope to the host window ({ScopeTypeName}.{RecommendedScope}, recommended)");
            RegisterScopeFix(context, root, invocation, diagnostic, ExplicitScope, scopeTypeName, useNamedArgument,
                $"Keep process-wide scope ({ScopeTypeName}.{ExplicitScope}, explicit)");
        }
    }

    private static void RegisterScopeFix(
        CodeFixContext context,
        SyntaxNode root,
        InvocationExpressionSyntax invocation,
        Diagnostic diagnostic,
        string scopeMember,
        string scopeTypeName,
        bool useNamedArgument,
        string title)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                ct =>
                {
                    var argument = SyntaxFactory.Argument(
                        SyntaxFactory.ParseExpression($"{scopeTypeName}.{scopeMember}"));

                    if (useNamedArgument)
                    {
                        var nameColon = SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(ScopeParameterName))
                            .WithColonToken(SyntaxFactory.Token(SyntaxKind.ColonToken)
                                .WithTrailingTrivia(SyntaxFactory.Space));
                        argument = argument.WithNameColon(nameColon);
                    }

                    argument = argument.WithLeadingTrivia(SyntaxFactory.Space);

                    var newArgumentList = invocation.ArgumentList.WithArguments(
                        invocation.ArgumentList.Arguments.Add(argument));
                    var newInvocation = invocation.WithArgumentList(newArgumentList);

                    var newRoot = root.ReplaceNode(invocation, newInvocation);
                    return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                },
                equivalenceKey: UsePersistedScopeAnalyzer.DiagnosticId + ":" + scopeMember),
            diagnostic);
    }
}
