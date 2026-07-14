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
/// Code fix for <see cref="ControlledInputAnalyzer"/> (<c>REACTOR_HOOKS_011</c>).
/// For controls that expose a read-only modifier (TextBox, RatingControl) it wraps
/// the factory call — <c>TextBox(name, _ =&gt; { })</c> → <c>TextBox(name, _ =&gt; { }).IsReadOnly(true)</c>
/// — making the "display only" intent explicit. Controls with no <c>IsReadOnly</c>
/// modifier get no auto-fix (nudge only); the analyzer signals fix availability via
/// <see cref="ControlledInputAnalyzer.ReadOnlyModifierProperty"/>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ControlledInputCodeFix))]
[Shared]
public sealed class ControlledInputCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ControlledInputAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        SemanticModel? semanticModel = null;

        foreach (var diagnostic in context.Diagnostics)
        {
            // Only controls that expose an IsReadOnly modifier carry this property;
            // for the rest the diagnostic stands as a nudge with no auto-fix.
            if (!diagnostic.Properties.TryGetValue(ControlledInputAnalyzer.ReadOnlyModifierProperty, out var modifier)
                || string.IsNullOrEmpty(modifier))
                continue;

            if (root.FindNode(diagnostic.Location.SourceSpan) is not InvocationExpressionSyntax invocation)
                continue;

            // Don't wrap when the chain already sets read-only-ness (e.g. an explicit
            // .IsReadOnly(false)): appending .IsReadOnly(true) would produce contradictory
            // modifiers whose last-writer value stays false, silently silencing the warning
            // without making the control read-only. An explicit .IsReadOnly(false) also
            // signals the author wants it editable — so the right move is to wire the
            // callback (which we can't synthesize), not force read-only. Nudge only.
            if (ControlledInputAnalyzer.HasIsReadOnlyModifier(invocation))
                continue;

            var modifierName = modifier!;

            // Only offer the fix when the read-only modifier extension actually resolves in
            // scope at this call site. The analyzer fires on a fully-qualified factory call
            // (e.g. Microsoft.UI.Reactor.Factories.TextBox(...)) even without
            // `using Microsoft.UI.Reactor;`, but .IsReadOnly(...) is an extension method that
            // only binds when that namespace is imported — appending it otherwise would emit
            // non-compiling code. If it isn't in scope, nudge only.
            semanticModel ??= await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
            if (semanticModel is null) continue;
            if (!IsModifierInScope(semanticModel, invocation, modifierName, context.CancellationToken))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Make read-only intent explicit with .{modifierName}(true)",
                    ct =>
                    {
                        // Clear only the receiver's leading/trailing edge trivia (the
                        // wrapped node re-applies the invocation's edges below), leaving
                        // any trivia inside the argument list untouched.
                        var receiver = invocation.WithLeadingTrivia().WithTrailingTrivia();

                        var wrapped = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                receiver,
                                SyntaxFactory.IdentifierName(modifierName)),
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Argument(
                                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)))))
                            .WithTriviaFrom(invocation);

                        var newRoot = root.ReplaceNode(invocation, wrapped);
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: ControlledInputAnalyzer.DiagnosticId + ":" + modifierName),
                diagnostic);
        }
    }

    /// <summary>
    /// True when the read-only modifier (an extension method reduced against the factory
    /// call's element type) resolves in scope at the call site — i.e. its declaring Reactor
    /// namespace is imported. Prevents the fix from emitting <c>.IsReadOnly(...)</c> where it
    /// would not bind (a fully-qualified factory call without <c>using Microsoft.UI.Reactor;</c>).
    /// </summary>
    private static bool IsModifierInScope(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        string modifierName,
        System.Threading.CancellationToken cancellationToken)
    {
        if (semanticModel.GetTypeInfo(invocation, cancellationToken).Type is not { } elementType)
            return false;

        return semanticModel
            .LookupSymbols(invocation.SpanStart, container: elementType, name: modifierName, includeReducedExtensionMethods: true)
            .Any(symbol => symbol is IMethodSymbol { IsExtensionMethod: true } method
                && CommandDebounceAnalyzer.IsReactorNamespace(method.ContainingNamespace?.ToDisplayString()));
    }
}
