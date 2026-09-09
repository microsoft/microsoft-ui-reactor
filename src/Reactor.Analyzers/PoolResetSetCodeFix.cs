using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Code fix for REACTOR_POOL_001 / REACTOR_MOD_002: rewrites
/// <c>x.Set(fe =&gt; fe.PROP = VALUE)</c> to <c>x.PROP(VALUE)</c> using the corresponding
/// Reactor modifier. Attached-property writes take the same route —
/// <c>x.Set(b =&gt; ToolTipService.SetToolTip(b, "Save"))</c> becomes <c>x.ToolTip("Save")</c>.
/// Block-body lambdas are handled too, including multi-statement and mixed ones —
/// <c>fe =&gt; { fe.A = 1; Owner.SetB(fe, 2); }</c> becomes <c>.A(1).B(2)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Where the modifier signature differs from the raw property type, the codefix
/// translates the RHS into the shape that reads best (see <c>Margin</c>
/// below — a literal <c>new Thickness(8)</c> becomes <c>.Margin(8)</c> rather
/// than passing the struct through). When no safe translation exists, the
/// codefix is suppressed — the analyzer still reports the trap, the developer
/// just has to fix by hand. For attached properties that decision is the
/// analyzer's (<c>FixablePropertiesKey</c>), because part of it is semantic.
/// </para>
/// <para>
/// Multi-statement bodies are converted all-or-nothing; see
/// <see cref="TryBuildChain"/> for why a partial extraction is not offered, and
/// <see cref="TryBuildCarriedTrivia"/> for how comments are preserved.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PoolResetSetCodeFix))]
[Shared]
public sealed class PoolResetSetCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            PoolResetSetAnalyzer.DiagnosticId,
            PoolResetSetAnalyzer.ModifierAvailableDiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        // Each diagnostic carries the complete reported set for its invocation, so one is
        // enough to rewrite the whole `.Set(...)`. Dedupe by span so a block body — which
        // reports once per convertible assignment — produces a single chain fix rather than N
        // competing fixes that each want to replace the same node.
        var seen = new HashSet<TextSpan>();

        foreach (var diagnostic in context.Diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            if (!seen.Add(span)) continue;

            var node = root.FindNode(span);
            if (node is not InvocationExpressionSyntax invocation) continue;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;

            // The rewrite deletes the whole invocation. If it spans a #if/#region, the
            // surviving directives would be left unbalanced.
            if (invocation.ContainsDirectives) continue;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count != 1) continue;

            // Only the properties the analyzer actually reported are safe to convert — it
            // applied the receiver gates, this fix does not repeat them. The fixable subset is
            // narrower still: several attached properties are worth diagnosing but have no
            // mechanical rewrite (see FixablePropertiesKey).
            if (!diagnostic.Properties.TryGetValue(PoolResetSetAnalyzer.ReportedPropertiesKey, out var packed)
                || string.IsNullOrEmpty(packed))
            {
                continue;
            }
            var reported = new HashSet<string>(packed!.Split(','), StringComparer.Ordinal);

            diagnostic.Properties.TryGetValue(PoolResetSetAnalyzer.FixablePropertiesKey, out var packedFixable);
            var fixable = string.IsNullOrEmpty(packedFixable)
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(packedFixable!.Split(','), StringComparer.Ordinal);

            // Fully-convertible only: every statement must be accounted for, or the rewrite
            // would delete the ones it did not recognise.
            var writes = SetLambdaHelpers.GetFullyConvertibleLambdaBody(args[0].Expression);
            if (writes.IsDefaultOrEmpty) continue;

            var steps = TryBuildChain(writes, reported, fixable);
            if (steps is null) continue; // Mixed or untranslatable body — leave the diagnostic unfixed.

            if (ReceiverAlreadyApplies(memberAccess.Expression, steps))
                continue; // Rewriting would invert precedence — see the method's remarks.

            var carriedTrivia = TryBuildCarriedTrivia(args[0].Expression, writes);
            if (carriedTrivia is null) continue; // A comment with nowhere safe to go.

            var title = steps.Count == 1
                ? $"Use .{steps[0].Modifier}() modifier"
                : $"Use .{string.Join("().", steps.Select(s => s.Modifier))}() modifiers";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    ct =>
                    {
                        // Chain in source order so any ordering the author relied on between
                        // the writes is preserved.
                        ExpressionSyntax rewritten = memberAccess.Expression;
                        for (var i = 0; i < steps.Count; i++)
                        {
                            // Trivia that preceded the statement hangs off the END of what came
                            // before, not the start of this step — the same reason Roslyn puts it
                            // on the separator comma. Attaching it to the dot instead would make
                            // the formatter treat a same-line trailing comment as leading the next
                            // call and move it onto its own line.
                            var (preceding, own) = carriedTrivia[i];
                            if (preceding.Count > 0)
                            {
                                rewritten = rewritten.WithTrailingTrivia(
                                    rewritten.GetTrailingTrivia().AddRange(preceding));
                            }

                            var dot = SyntaxFactory.Token(SyntaxKind.DotToken).WithLeadingTrivia(own);

                            rewritten = SyntaxFactory.InvocationExpression(
                                SyntaxFactory.MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    rewritten,
                                    dot,
                                    SyntaxFactory.IdentifierName(steps[i].Modifier)),
                                steps[i].Arguments);
                        }

                        var newRoot = root.ReplaceNode(invocation, rewritten.WithTriviaFrom(invocation));
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: "ReactorModifierChain:" + string.Join(",", steps.Select(s => s.Modifier))),
                diagnostic);
        }
    }

    /// <summary>
    /// Work out the trivia to place before each modifier call's <c>.</c>, or <c>null</c> when
    /// a comment in the body has nowhere safe to go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Follows Roslyn's own N-statements-to-one-expression fix rather than inventing a scheme:
    /// <c>CSharpUseObjectInitializerCodeFixProvider.CreateExpressions</c> carries each matched
    /// statement's leading trivia onto the initializer element that statement becomes. The
    /// fluent-chain analogue is the <c>.</c> that introduces each modifier call — a legal
    /// comment position, and safe for <c>//</c> because the statement's original line breaks
    /// travel with it.
    /// </para>
    /// <para>
    /// Carried all-or-nothing so the common uncommented body still collapses onto one line:
    /// with no comments anywhere the chain takes no trivia at all, and a single comment makes
    /// every step take its original line break, yielding a formatted multi-line chain instead
    /// of one long line with a comment wedged into it.
    /// </para>
    /// <para>
    /// Roslyn can additionally park its last statement's <em>trailing</em> trivia on the last
    /// element, because an initializer is followed by <c>}</c>. A chain is not: a trailing
    /// <c>//</c> on the final statement would land immediately before the enclosing <c>;</c>
    /// and comment it out. A trailing comment on any <em>earlier</em> statement is fine — it
    /// rides along on the next step's <c>.</c> and stays on the line the author wrote it on —
    /// but anything that cannot be placed exactly declines the fix, the same all-or-nothing
    /// rule the rest of this fix follows.
    /// </para>
    /// </remarks>
    private static List<(SyntaxTriviaList Preceding, SyntaxTriviaList Own)>? TryBuildCarriedTrivia(
        ExpressionSyntax lambda,
        ImmutableArray<SetLambdaHelpers.SetBodyWrite> writes)
    {
        var perStatement = new List<(SyntaxTriviaList Preceding, SyntaxTriviaList Own)>(writes.Length);

        // Spans must be gathered from the ORIGINAL attached lists. A list rebuilt with AddRange
        // is detached and its positions restart at zero, so spans taken from a combined list
        // would never match the ones the tree walk below reports.
        var carried = new HashSet<TextSpan>();

        // Block bodies hang their trivia off the statement; an expression body has none
        // of its own worth carrying (it is on the invocation, which keeps it).
        foreach (var owner in writes.Select(write =>
            write.Node.Parent is ExpressionStatementSyntax statement ? (SyntaxNode)statement : write.Node))
        {
            // The preceding token's trailing trivia matters as much as the statement's own
            // leading trivia: it holds the line break (and any same-line trailing comment) that
            // makes the '//' safe and the chain readable. That token is '{' for the first
            // statement and the previous statement's ';' after that, so one lookup covers both.
            var precedingTrailing = owner.GetFirstToken().GetPreviousToken().TrailingTrivia;
            var ownLeading = owner.GetLeadingTrivia();

            // Explicit Where rather than an inner `if`: keeps the filter in the sequence so the
            // intent reads as "carry the comments" (CodeQL cs/linq/missed-where).
            carried.UnionWith(precedingTrailing.Where(IsComment).Select(trivia => trivia.Span));
            carried.UnionWith(ownLeading.Where(IsComment).Select(trivia => trivia.Span));

            perStatement.Add((precedingTrailing, ownLeading));
        }

        if (carried.Count == 0)
        {
            // Nothing to preserve: keep the chain compact rather than reflowing it.
            if (lambda.DescendantTrivia().Any(IsComment))
                return null;

            return perStatement.ConvertAll(_ => (default(SyntaxTriviaList), default(SyntaxTriviaList)));
        }

        // Every comment in the body must be one we are about to carry. Anything else — a
        // trailing comment on the final statement, one dangling before the closing brace, one
        // inside the lambda header or an assignment — would be dropped by the rewrite.
        if (lambda.DescendantTrivia().Where(IsComment).Any(trivia => !carried.Contains(trivia.Span)))
            return null;

        return perStatement;
    }

    private static bool IsComment(SyntaxTrivia trivia)
        => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    /// <summary>
    /// Build the ordered modifier chain replacing a whole <c>.Set(...)</c> body, or
    /// <c>null</c> when the body cannot be converted in full.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately all-or-nothing. Converting <em>every</em> statement removes the
    /// <c>.Set</c> entirely, which is exactly N applications of the long-standing
    /// single-assignment rewrite and carries no new risk.
    /// </para>
    /// <para>
    /// A partial extraction would be different in kind: it leaves a residual <c>.Set</c>, and
    /// the extracted write moves from the setter phase into the modifier phase — so its order
    /// relative to the statements left behind changes. That is harmless for independent
    /// properties but cannot be shown safe in general (the reconciler has real
    /// order-sensitive pairs, e.g. TextBox <c>AcceptsReturn</c> before <c>Text</c>). So a
    /// mixed body keeps its diagnostic and gets no automatic fix.
    /// </para>
    /// <para>
    /// Assumes <paramref name="writes"/> came from
    /// <see cref="SetLambdaHelpers.GetFullyConvertibleLambdaBody"/>, which has already
    /// established that they target the lambda parameter and that they account for every
    /// statement in the body.
    /// </para>
    /// </remarks>
    private static List<(string Modifier, ArgumentListSyntax Arguments, string[]? Conflicts)>? TryBuildChain(
        ImmutableArray<SetLambdaHelpers.SetBodyWrite> writes,
        HashSet<string> reported,
        HashSet<string> fixable)
    {
        var steps = new List<(string Modifier, ArgumentListSyntax Arguments, string[]? Conflicts)>();

        foreach (var write in writes)
        {
            // Not reported means the analyzer gated it out (wrong control type for the
            // modifier, or no modifier at all). Not fixable means it is a real diagnostic with
            // no mechanical rewrite. Converting either would be the silent no-op — or the
            // uncompilable call — the gates exist to prevent.
            if (!reported.Contains(write.Key)) return null;
            if (!fixable.Contains(write.Key)) return null;

            string modifier;
            string[]? conflicts;
            ArgumentListSyntax? modifierArgs;

            if (write.AttachedCall is not null)
            {
                if (!ModifierTable.AttachedBySetter.TryGetValue(write.Key, out var attached)) return null;
                modifier = attached.Modifier;
                conflicts = attached.ReceiverConflicts;
                // Every auto-fixable attached entry passes its value straight through; the
                // ones needing a translation are marked AutoFix: false in the table.
                modifierArgs = SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(write.Value)));
            }
            else
            {
                if (!ModifierTable.Properties.TryGetValue(write.Key, out var info)) return null;
                modifier = info.Modifier;
                conflicts = null;
                modifierArgs = TryBuildModifierArguments(write.Key, write.Value);
            }

            if (modifierArgs is null) return null;

            steps.Add((modifier, modifierArgs, conflicts));
        }

        return steps.Count == 0 ? null : steps;
    }

    /// <summary>
    /// True when the receiver chain already calls one of the modifiers we are about to append,
    /// which makes the rewrite a behaviour change rather than a refactor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Setters and modifiers run in different phases, and modifiers run <em>second</em>:
    /// <c>ApplySetters</c> is called from inside the mount/update dispatch
    /// (<c>DescriptorHandler</c> and friends), while <c>ApplyModifiers</c> is called after that
    /// dispatch returns (<c>Reconciler.Mount.cs</c>, <c>Reconciler.Update.cs</c>). So a modifier
    /// beats a <c>.Set</c> write of the same property regardless of their order in the chain:
    /// </para>
    /// <code>
    /// b.IsEnabled(true).Set(c =&gt; c.IsEnabled = false)   // renders true — the modifier wins
    /// b.IsEnabled(true).IsEnabled(false)                 // renders false — last call wins
    /// </code>
    /// <para>
    /// Only a modifier appearing <em>before</em> the <c>.Set</c> is affected, and that is
    /// exactly what the receiver expression contains, so walking it is sufficient. Any part of
    /// the chain we cannot see (a local, a helper's return value) is left alone — this is a
    /// syntactic guard for the visible case, not a whole-program analysis.
    /// </para>
    /// <para>
    /// A name match is not always enough: some modifiers write more than the property they are
    /// named after. <c>.ToolTip(tip, placement)</c> also writes
    /// <c>ToolTipService.Placement</c>, and <c>.AccessibilityHidden()</c> is shorthand for
    /// <c>.AccessibilityView(Raw)</c>. Those aliases come from the table entry's
    /// <c>ReceiverConflicts</c>.
    /// </para>
    /// </remarks>
    private static bool ReceiverAlreadyApplies(
        ExpressionSyntax receiver,
        List<(string Modifier, ArgumentListSyntax Arguments, string[]? Conflicts)> steps)
    {
        for (var node = receiver; node is InvocationExpressionSyntax call;)
        {
            if (call.Expression is not MemberAccessExpressionSyntax access)
                break;

            var called = access.Name.Identifier.Text;
            if (steps.Exists(step =>
                string.Equals(step.Modifier, called, StringComparison.Ordinal)
                || (step.Conflicts is not null
                    && Array.IndexOf(step.Conflicts, called) >= 0)))
            {
                return true;
            }

            node = access.Expression;
        }

        return false;
    }

    /// <summary>
    /// Build the argument list for the modifier call, translating the RHS when
    /// the modifier signature differs from the raw FE property type.
    /// </summary>
    /// <returns>
    /// The argument list to pass to the modifier, or <c>null</c> if no safe
    /// translation is possible (in which case no codefix is registered).
    /// </returns>
    private static ArgumentListSyntax? TryBuildModifierArguments(string propName, ExpressionSyntax value)
    {
        // Margin/Padding/BorderThickness are Thickness-typed properties, and CornerRadius is
        // the same shape with a CornerRadius struct. All four modifiers accept both the raw
        // struct and a decomposed set of doubles, so two rewrites are available and the
        // decomposed one reads better:
        //   new Thickness(uniform)      → .Padding(uniform)
        //   new Thickness(l, t, r, b)   → .Padding(l, t, r, b)
        // Any other RHS — an opaque local, a field, a call, a ternary, `new Thickness()` —
        // rides the struct-typed overload verbatim.
        if (propName is "Margin" or "Padding" or "BorderThickness" or "CornerRadius")
        {
            var structName = propName == "CornerRadius" ? "CornerRadius" : "Thickness";

            // Both spellings of a constructor literal qualify. The target-typed `new(8)` form
            // names no type, but the property being assigned fixes it, and only the arguments
            // survive the rewrite either way.
            //
            // An object initializer disqualifies both: `new Thickness(8) { Left = 5 }` is
            // Thickness(5,8,8,8), so decomposing to the constructor arguments alone would
            // silently drop the initializer and change the value written — the exact class of
            // silent behaviour change this diagnostic exists to prevent. The explicitly-typed
            // form then falls through and rides the struct overload verbatim, initializer and
            // all; the target-typed form has no such escape and is refused below.
            SeparatedSyntaxList<ArgumentSyntax>? ctorArgs = value switch
            {
                ObjectCreationExpressionSyntax { Initializer: null } oce when IsNamedType(oce.Type, structName)
                    => oce.ArgumentList?.Arguments,
                ImplicitObjectCreationExpressionSyntax { Initializer: null } ioce
                    => ioce.ArgumentList.Arguments,
                _ => null,
            };

            // Both structs have 0/1/4-arg constructors. The 0-arg form is not interesting;
            // 1 and 4 map cleanly onto the uniform and per-edge modifier overloads.
            if (ctorArgs is { Count: 1 or 4 })
                return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(ctorArgs.Value));

            // A target-typed `new(...)` that did not decompose must not fall through to the
            // struct overload: it carries no type of its own, so it is convertible to
            // `double` as readily as to the struct and `.Margin(new())` would be an
            // ambiguous call. (`default` needs no such guard — the analyzer's
            // IsNullOrDefault gate drops null/default right-hand sides before they are ever
            // reported, so they never reach this method.)
            if (value.IsKind(SyntaxKind.ImplicitObjectCreationExpression))
                return null;
        }

        // Everything reaching here — the four struct-typed properties in their pass-through
        // form, plus all other tracked properties, whose modifier accepts the same type as
        // the property (double / enum / string / Brush) — takes the RHS unchanged.
        return SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(value)));
    }

    private static bool IsNamedType(TypeSyntax type, string simpleName) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text == simpleName,
        QualifiedNameSyntax q => q.Right.Identifier.Text == simpleName,
        _ => false,
    };
}
