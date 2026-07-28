using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_DSL_001</c> — when a LINQ <c>Select</c> projects to Reactor
/// elements and the result is materialized into a layout container's children
/// (<c>VStack</c>, <c>HStack</c>, <c>FlexRow</c>, <c>FlexColumn</c>, <c>Grid</c>, ...),
/// every projected element should call <c>.WithKey(...)</c>. Without keys, the
/// reconciler matches positionally and re-mounts every row on insert / reorder
/// — losing focus, animation state, and ElementRef identity.
///
/// Heuristic: a <c>Select(x =&gt; expr)</c> invocation whose lambda body
/// (a) returns a Reactor element-shaped expression, and
/// (b) contains no <c>.WithKey(</c> token anywhere in the lambda body.
///
/// Conservative — fires only on `.Select(...)` whose lambda body's outermost
/// expression is an invocation (the typical "row factory" pattern).
///
/// <para><c>REACTOR_DSL_002</c> is the complement: a key that is *present but
/// non-stable*. DSL_001 stays silent as soon as any <c>.WithKey(</c> exists, so
/// DSL_002 inspects the key <em>expression</em> of each <c>.WithKey(arg)</c> that
/// sits inside a <c>Select</c>/<c>ForEach</c> projection lambda and flags two
/// shapes: (1) a positional key whose only referenced lambda parameter is the
/// index (never the item), and (2) a per-render-random key built from
/// <c>Guid.NewGuid()</c>, <c>DateTime.Now</c>/<c>UtcNow</c>, <c>Random</c>, or
/// <c>Environment.TickCount</c>. Both re-mount rows on insert/reorder just like a
/// missing key. Info severity + no fix: a positional key is only wrong for lists
/// that reorder/insert, and the framework can't synthesize the real identity.</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingWithKeyAnalyzer : DiagnosticAnalyzer
{
    public const string Id = "REACTOR_DSL_001";
    public const string NonStableKeyId = "REACTOR_DSL_002";

    private static readonly DiagnosticDescriptor Rule = new(
        Id,
        "Dynamic list item missing .WithKey",
        "Element produced by Select(...) doesn't call .WithKey(...). Without a key, the reconciler matches by position and re-mounts every row on insert/reorder, losing focus, animation, and ElementRef state.",
        "Reactor.Dsl",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Per SKILL.md gotcha #6 — every dynamic list item should carry a stable key via .WithKey(id). The reconciler uses keys to match elements across renders. Without them, inserting at the head of a list re-mounts every row.");

    private static readonly DiagnosticDescriptor NonStableKeyRule = new(
        NonStableKeyId,
        "Non-stable list key in .WithKey",
        ".WithKey(...) uses a non-stable key (the list index or a per-render value such as Guid.NewGuid()/DateTime.Now/UtcNow/Random/Environment.TickCount). On insert/reorder the reconciler re-mounts every row — losing focus, animation, and ElementRef state — the same failure a missing key causes. Key off the item's stable id instead.",
        "Reactor.Dsl",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A positional key (the Select/ForEach index parameter) or a per-render-random key identifies a slot, not a row: it is identical to — or worse than — no key when items are inserted or reordered. Prefer a value carried by the data, e.g. .WithKey(item.Id).");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule, NonStableKeyRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    static void Analyze(SyntaxNodeAnalysisContext ctx)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;

        if (inv.Expression is not MemberAccessExpressionSyntax member) return;
        var methodName = member.Name.Identifier.ValueText;

        // REACTOR_DSL_002 — a present-but-non-stable key. The analysis is
        // triggered by each `.WithKey(...)` invocation (not the enclosing
        // Select), so every key is inspected exactly once even when Selects
        // nest; the diagnostic itself is reported on the key expression.
        if (methodName == "WithKey")
        {
            AnalyzeNonStableKey(ctx, inv);
            return;
        }

        // REACTOR_DSL_001 — a missing key on a Select projection.
        if (methodName == "Select")
        {
            AnalyzeMissingKey(ctx, inv);
        }
    }

    // <snippet:with-key-rule>
    static void AnalyzeMissingKey(SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax inv)
    {
        // Single lambda argument with an invocation body.
        if (inv.ArgumentList.Arguments.Count != 1) return;
        if (inv.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda) return;

        var body = lambda.Body;
        if (body is BlockSyntax block) body = ExtractReturnExpression(block) ?? body;
        if (body is not InvocationExpressionSyntax) return;

        // Cheap textual probe — analyzers run hot, so avoid full symbol resolution.
        // If the lambda body mentions ".WithKey(" anywhere, assume it's keyed.
        var bodyText = body.ToString();
        if (bodyText.Contains(".WithKey(")) return;
        // </snippet:with-key-rule>

        // Only flag when the result is consumed as children of a layout factory
        // (VStack / HStack / FlexRow / FlexColumn / Grid / ScrollView). This
        // keeps false positives out of generic LINQ that just happens to project
        // to elements (e.g., to a List<Element>).
        if (!IsConsumedAsLayoutChildren(inv)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(Rule, inv.GetLocation()));
    }

    // REACTOR_DSL_002 — inspect the key expression of a `.WithKey(arg)` call.
    // Purely syntactic (no GetSymbolInfo): identifiers are matched by name
    // against the enclosing Select/ForEach lambda's parameters, and the
    // per-render-random sources are matched by their well-known type and
    // member names.
    static void AnalyzeNonStableKey(SyntaxNodeAnalysisContext ctx, InvocationExpressionSyntax withKeyInv)
    {
        // WithKey takes exactly one argument (the key).
        if (withKeyInv.ArgumentList.Arguments.Count != 1) return;
        var arg = withKeyInv.ArgumentList.Arguments[0].Expression;

        // Scope to list items: the WithKey must live inside a Select/ForEach
        // projection lambda. A key on a single, static element never reorders,
        // and the `.WithKey` anchor keeps this off unrelated fluent chains.
        var lambda = EnclosingProjectionLambda(withKeyInv);
        if (lambda is null) return;

        // Shape 2 — a per-render-random key (regenerates every render, so it
        // matches nothing across renders). Independent of parameter count.
        if (ContainsPerRenderValue(arg))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(NonStableKeyRule, arg.GetLocation()));
            return;
        }

        // Shape 1 — a positional key. Needs a two-parameter (item, index)
        // projection lambda; fires only when the key references the index
        // parameter and never the item parameter (so composites like
        // $"{item.Id}-{i}" — which reference the item too — are left alone).
        if (!TryGetItemAndIndexParameters(lambda, out var itemName, out var indexName)) return;

        var referenced = ReferencedIdentifierNames(arg);
        if (referenced.Contains(indexName) && !referenced.Contains(itemName))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(NonStableKeyRule, arg.GetLocation()));
        }
    }

    // Walk up to the nearest enclosing lambda and return it only when that
    // lambda is the projection argument to a LINQ `Select` or Reactor's `ForEach`
    // factory (per the per-branch conditions below). Returns null when the
    // WithKey sits in some other (non-projection) lambda or none at all.
    static LambdaExpressionSyntax? EnclosingProjectionLambda(SyntaxNode node)
    {
        for (var cur = node.Parent; cur is not null; cur = cur.Parent)
        {
            if (cur is LambdaExpressionSyntax lambda)
            {
                if (lambda.Parent is ArgumentSyntax arg
                    && arg.Parent is ArgumentListSyntax argList
                    && argList.Parent is InvocationExpressionSyntax outer)
                {
                    var name = SimpleName(outer.Expression);
                    // LINQ Select — `collection.Select(lambda)`; the projection
                    // lambda is the (first) argument.
                    if (name == "Select") return lambda;
                    // Reactor's ForEach factory only: a bare `ForEach(items, lambda)`
                    // imported via `using static …Factories`, or a
                    // `Factories.ForEach(items, lambda)` receiver — with the
                    // collection leading, so the lambda is never argument 0.
                    // Restricting to that shape avoids matching unrelated ForEach
                    // APIs: the BCL `list.ForEach(action)`, `Parallel.ForEach(
                    // source, body)`, or any custom `X.ForEach(items, lambda)`.
                    if (name == "ForEach"
                        && argList.Arguments.IndexOf(arg) >= 1
                        && IsReactorForEachReceiver(outer.Expression))
                        return lambda;
                }
                return null;
            }
        }
        return null;
    }

    // The receiver shape of Reactor's static `ForEach` factory: either a bare
    // identifier (`using static …Factories; ForEach(...)`) or a member access
    // whose immediate receiver is `Factories` (`Factories.ForEach(...)` /
    // `…Factories.ForEach(...)`).
    static bool IsReactorForEachReceiver(ExpressionSyntax invoked) => invoked switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax m => SimpleName(m.Expression) == "Factories",
        _ => false,
    };

    // A projection lambda carries a positional index only in its two-parameter
    // form: `(item, index) => …` (Select/ForEach both expose that overload).
    static bool TryGetItemAndIndexParameters(LambdaExpressionSyntax lambda, out string itemName, out string indexName)
    {
        itemName = indexName = string.Empty;
        if (lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } paren)
        {
            itemName = paren.ParameterList.Parameters[0].Identifier.ValueText;
            indexName = paren.ParameterList.Parameters[1].Identifier.ValueText;
            return itemName.Length > 0 && indexName.Length > 0;
        }
        return false;
    }

    // Identifiers that read a value: excludes the right-hand `.Name` of a member
    // access (a member/property name is not a reference to a lambda parameter).
    static HashSet<string> ReferencedIdentifierNames(ExpressionSyntax arg)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in arg.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (id.Parent is MemberAccessExpressionSyntax mae && mae.Name == id) continue;
            names.Add(id.Identifier.ValueText);
        }
        return names;
    }

    // True when the key expression contains a per-render-random source:
    // Guid.NewGuid(), DateTime.Now/UtcNow, Environment.TickCount(64), or the
    // Random type used to produce a value — matched only as `new Random(...)` or
    // `Random.Shared` (the sole static randomness source). Requiring those exact
    // shapes — rather than any identifier or member access named "Random" —
    // avoids flagging a local, parameter, or an unrelated `Foo.Random.Bar`.
    static bool ContainsPerRenderValue(ExpressionSyntax arg)
    {
        foreach (var node in arg.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax mae:
                    var memberName = mae.Name.Identifier.ValueText;
                    var receiver = SimpleName(mae.Expression);
                    if (receiver == "Guid" && memberName == "NewGuid") return true;
                    if (receiver == "DateTime" && memberName is "Now" or "UtcNow") return true;
                    if (receiver == "Environment" && memberName is "TickCount" or "TickCount64") return true;
                    // Random.Shared — the only static randomness source on the
                    // Random type (instance use is caught via `new Random(...)`
                    // below). Requiring the member name avoids flagging an
                    // unrelated `Foo.Random.Bar` whose receiver merely reads a
                    // member named Random.
                    if (receiver == "Random" && memberName == "Shared") return true;
                    break;

                // new Random(…) / new System.Random(…).
                case ObjectCreationExpressionSyntax oce when SimpleName(oce.Type) == "Random":
                    return true;
            }
        }
        return false;
    }

    // Rightmost simple name of an expression: for `Guid` (IdentifierName) →
    // "Guid"; for `System.Guid` / `items.Select` (MemberAccess) or `System.Random`
    // (QualifiedName, in type position) → the trailing name. Lets a bare and a
    // qualified/instance form match by the same name.
    static string? SimpleName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        QualifiedNameSyntax q => q.Right.Identifier.ValueText,
        _ => null,
    };

    static ExpressionSyntax? ExtractReturnExpression(BlockSyntax block)
    {
        // Single-statement `return X;` — walk it. Multi-statement bodies are
        // out of scope for this conservative pass.
        if (block.Statements.Count != 1) return null;
        return block.Statements[0] is ReturnStatementSyntax ret ? ret.Expression : null;
    }

    // Layout factories whose children-array overload is the typical receiver
    // for `Select(...)` row-factories. ScrollView is intentionally excluded —
    // it takes a single child, not Element[]. WrapGrid (not WrapPanel) is the
    // correct factory name in Reactor.
    static readonly System.Collections.Generic.HashSet<string> LayoutFactories = new(System.StringComparer.Ordinal)
    {
        "VStack", "HStack", "FlexRow", "FlexColumn", "Flex", "Grid", "WrapGrid",
    };

    static bool IsConsumedAsLayoutChildren(InvocationExpressionSyntax selectInv)
    {
        // Walk up: Select → optional .ToArray()/.ToList()/.ToArray<Element>() → Argument → Invocation
        SyntaxNode? cur = selectInv;
        while (cur?.Parent is MemberAccessExpressionSyntax m && m.Parent is InvocationExpressionSyntax chain)
        {
            var name = m.Name.Identifier.ValueText;
            if (name is "ToArray" or "ToList") cur = chain;
            else break;
        }

        if (cur?.Parent is not ArgumentSyntax arg) return false;
        if (arg.Parent is not ArgumentListSyntax argList) return false;
        if (argList.Parent is not InvocationExpressionSyntax outer) return false;

        var outerName = outer.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax mae => mae.Name.Identifier.ValueText,
            _ => null,
        };

        return outerName is not null && LayoutFactories.Contains(outerName);
    }
}
