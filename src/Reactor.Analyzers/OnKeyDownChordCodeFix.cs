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
/// Template code fix for <see cref="OnKeyDownChordAnalyzer"/> (<c>REACTOR_INPUT_001</c>).
/// </summary>
/// <remarks>
/// <para>
/// Rewriting a focus-scoped <c>.OnKeyDown</c> chord into an app-wide <c>Command</c> accelerator is
/// <b>intent-heavy</b>: the command belongs wherever the app registers its shortcuts (not on the
/// element), its <c>Execute</c> body has to be lifted out of a handler that closes over the event
/// args, and only the author knows the command's label/scope. There is therefore no safe, fully
/// mechanical rewrite. This fix is a <b>template/preview</b>: it appends a single-line scaffold
/// comment to the offending call showing the exact <c>new Command { …, Accelerator =
/// Accelerator(VirtualKey.S, VirtualKeyModifiers.Control) }</c> shape to write. The concrete
/// <c>VirtualKey</c> and the full Ctrl/Alt modifier set come from <see cref="Diagnostic.Properties"/>
/// (populated by the analyzer), so the scaffold matches exactly what was detected — including the
/// analyzer's nested-closure exclusion.
/// </para>
/// <para>
/// The fix is deliberately <b>additive</b>: it never edits executable code, so it can never drop a
/// handler's other key handling, break compilation on any receiver, or change runtime behavior. The
/// warning persists until the author migrates the shortcut and removes the <c>.OnKeyDown</c> chord.
/// Applying it is <b>idempotent</b>: it is not offered again once a scaffold is already present, so
/// re-invoking never stacks duplicate comments.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OnKeyDownChordCodeFix))]
[Shared]
public sealed class OnKeyDownChordCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OnKeyDownChordAnalyzer.DiagnosticId);

    // No FixAll: the scaffold is per-call context (each handler's key/modifiers differ), and the
    // fix is a template preview rather than a mechanical rewrite, so "fix all" carries no benefit.
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan);
            if (node is not InvocationExpressionSyntax invocation) continue;
            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "OnKeyDown" }) continue;

            // Idempotency: if a REACTOR_INPUT_001 scaffold is already appended to this call, don't
            // offer the fix again — re-applying would otherwise stack duplicate comments.
            if (invocation.GetTrailingTrivia().ToFullString().Contains(OnKeyDownChordAnalyzer.DiagnosticId))
                continue;

            var comment = BuildScaffoldComment(diagnostic);

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add Command-accelerator template (REACTOR_INPUT_001)",
                    ct => Task.FromResult(AppendComment(context.Document, root, invocation, comment)),
                    equivalenceKey: OnKeyDownChordAnalyzer.DiagnosticId),
                diagnostic);
        }
    }

    private static Document AppendComment(Document document, SyntaxNode root, InvocationExpressionSyntax invocation, string comment)
    {
        var newTrailing = invocation.GetTrailingTrivia()
            .Add(SyntaxFactory.Space)
            .Add(SyntaxFactory.Comment(comment));

        var newInvocation = invocation.WithTrailingTrivia(newTrailing);
        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }

    /// <summary>
    /// Builds the single-line block-comment scaffold using the concrete <c>VirtualKey</c> and the
    /// full Ctrl/Alt modifier expression the analyzer detected (handed over via
    /// <see cref="Diagnostic.Properties"/>). Falls back to safe defaults if a property is absent.
    /// </summary>
    private static string BuildScaffoldComment(Diagnostic diagnostic)
    {
        var key = Property(diagnostic, OnKeyDownChordAnalyzer.KeyProperty, "VirtualKey.<key>");
        var modifiers = Property(diagnostic, OnKeyDownChordAnalyzer.ModifiersProperty, "VirtualKeyModifiers.Control");

        return $"/* REACTOR_INPUT_001: .OnKeyDown is focus-scoped. Register this shortcut app-wide as a " +
               $"Command accelerator instead, e.g. new Command {{ Label = <name>, Execute = <handler>, " +
               $"Accelerator = Accelerator({key}, {modifiers}) }}, then remove this .OnKeyDown chord. */";
    }

    private static string Property(Diagnostic diagnostic, string name, string fallback) =>
        diagnostic.Properties.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value)
            ? value!
            : fallback;
}
