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
/// Code fix for REACTOR_WIN2D_001: appends <c>.UseSharedDevice()</c> to the outermost fluent
/// expression of the offending Win2D canvas, e.g.
/// <c>Win2DCanvas(draw).ClearColor(c)</c> → <c>Win2DCanvas(draw).ClearColor(c).UseSharedDevice()</c>.
/// </summary>
/// <remarks>
/// <c>.UseSharedDevice()</c> is an extension in <c>Microsoft.UI.Reactor.Advanced.Win2D</c>. That
/// namespace is almost always already imported at the fix site (the rule fires on a canvas paired
/// with the <c>UseCanvasResources</c> hook from the same namespace), but a caller could reach the
/// hook via a fully-qualified static call, so the fix adds the <c>using</c> when it is absent to
/// guarantee the appended call binds.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Win2DSharedDeviceCodeFix))]
[Shared]
public sealed class Win2DSharedDeviceCodeFix : CodeFixProvider
{
    private const string Title = "Append .UseSharedDevice() to the canvas";
    private const string Win2DNamespace = "Microsoft.UI.Reactor.Advanced.Win2D";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Win2DSharedDeviceAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            var factory = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (factory is null) continue;

            var outer = Win2DSharedDeviceAnalyzer.GetOutermostFluentInvocation(factory);

            context.RegisterCodeFix(
                CodeAction.Create(
                    Title,
                    _ => Task.FromResult(AppendUseSharedDevice(context.Document, root, outer)),
                    equivalenceKey: Win2DSharedDeviceAnalyzer.DiagnosticId),
                diagnostic);
        }
    }

    private static Document AppendUseSharedDevice(Document document, SyntaxNode root, InvocationExpressionSyntax outer)
    {
        // Determine whether the Win2D namespace is in scope AT THE FIX SITE (compilation unit or an
        // enclosing namespace of `outer`) before we rewrite the tree. A using in a sibling namespace
        // does not put the extension in scope here, so it must not suppress the insertion.
        var needsUsing = !IsWin2DNamespaceInScope(root, outer);

        // Keep the chain's leading trivia on the receiver and move its trailing trivia past the
        // appended call so `...Height(220)\n` becomes `...Height(220).UseSharedDevice()\n`.
        var trailing = outer.GetTrailingTrivia();
        var receiver = outer.WithoutTrailingTrivia();

        var appended = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver,
                SyntaxFactory.IdentifierName("UseSharedDevice")))
            .WithTrailingTrivia(trailing);

        var newRoot = root.ReplaceNode(outer, appended);

        if (needsUsing && newRoot is CompilationUnitSyntax compilationUnit)
        {
            var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(Win2DNamespace))
                .NormalizeWhitespace()
                .WithTrailingTrivia(DetectEndOfLine(root));
            // A compilation-unit using is in scope for every namespace in the file, so it fixes the
            // site regardless of which namespace block the canvas lives in.
            newRoot = compilationUnit.AddUsings(directive);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Returns the newline the document already uses (so the inserted using matches the file's
    /// convention — LF per the repo's .editorconfig — instead of forcing a fixed style).
    /// </summary>
    private static SyntaxTrivia DetectEndOfLine(SyntaxNode root)
    {
        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                return trivia;
        }

        return SyntaxFactory.LineFeed;
    }

    private static bool IsWin2DNamespaceInScope(SyntaxNode root, SyntaxNode anchor)
    {
        if (root is CompilationUnitSyntax compilationUnit && compilationUnit.Usings.Any(IsWin2DUsing))
            return true;

        for (var node = anchor.Parent; node is not null; node = node.Parent)
        {
            if (node is BaseNamespaceDeclarationSyntax ns && ns.Usings.Any(IsWin2DUsing))
                return true;
        }

        return false;
    }

    private static bool IsWin2DUsing(UsingDirectiveSyntax directive)
    {
        if (directive.Alias is not null || !directive.StaticKeyword.IsKind(SyntaxKind.None) || directive.Name is null)
            return false;

        // Accept `using global::Microsoft.UI.Reactor.Advanced.Win2D;` too, so an already-imported
        // namespace isn't mistaken for absent (which would insert a duplicate using / CS0105).
        var name = directive.Name.ToString();
        const string GlobalPrefix = "global::";
        if (name.StartsWith(GlobalPrefix, System.StringComparison.Ordinal))
            name = name.Substring(GlobalPrefix.Length);

        return name == Win2DNamespace;
    }
}
