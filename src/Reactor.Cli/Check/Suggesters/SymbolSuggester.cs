// Tier-2 Roslyn semantic suggester. Spec 038 §5.
//
// Covers the five highest-frequency CS-prefixed codes that touch Reactor types:
//
//   CS1061  - Member missing on type:        fuzzy-match receiver members.
//             Special case: if the missing name matches a parameter on an
//             enclosing factory call (e.g. `.OnClick(x)` on a `Button(...)`),
//             suggest the named-argument move.
//   CS0103  - Name not in scope:             fuzzy-match Reactor factory names.
//   CS0117  - No static member on type:      fuzzy-match static members.
//   CS1503  - Argument type mismatch:        special-case Element-vs-string,
//                                            Action-vs-Action<T>.
//   CS7036  - Wrong arg count for overload:  rank overloads by parameter-shape
//                                            distance, suggest closest as
//                                            named-arg form.
//
// Suggesters are pure: no I/O, no process spawning, no statics with mutable
// state. Tested by constructing CSharpCompilation in-memory.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Cli.Check.Suggesters;

internal sealed class SymbolSuggester : ISuggester
{
    public string Name => "SymbolSuggester";

    /// <summary>
    /// Legacy single-threshold fallback, retained for unit tests that
    /// pre-date <see cref="Thresholds"/>. New callers should rely on
    /// <see cref="Thresholds.For"/>.
    /// </summary>
    public const double DefaultThreshold = 0.75;

    public SuggestionResult Suggest(in SuggesterContext ctx)
    {
        var raw = SuggestRaw(in ctx);
        if (raw.Text is null) return SuggestionResult.Silent;
        var threshold = Thresholds.For(ctx.Diagnostic.Id);
        return raw.Confidence < threshold ? SuggestionResult.Silent : raw;
    }

    /// <summary>
    /// Produce the raw <see cref="SuggestionResult"/> for a context, applying
    /// the JaroWinkler similarity floor but NOT the per-code emit threshold.
    /// Used by the Phase-1 ship-gate threshold tuner so it can sweep T
    /// post-hoc; production callers should use <see cref="Suggest"/>.
    /// </summary>
    internal SuggestionResult SuggestRaw(in SuggesterContext ctx)
    {
        return ctx.Diagnostic.Id switch
        {
            "CS1061" => SuggestForCS1061(ctx),
            "CS0103" => SuggestForCS0103(ctx),
            "CS0117" => SuggestForCS0117(ctx),
            "CS1503" => SuggestForCS1503(ctx),
            "CS7036" => SuggestForCS7036(ctx),
            _ => SuggestionResult.Silent,
        };
    }

    // ── CS1061 ─────────────────────────────────────────────────────

    static SuggestionResult SuggestForCS1061(in SuggesterContext ctx)
    {
        if (ctx.Receiver is null || ctx.Node is null) return SuggestionResult.Silent;
        var memberName = ExtractMissingMemberName(ctx.Node);
        if (memberName is null) return SuggestionResult.Silent;

        // 1. Named-argument move: `.OnClick(x)` chained on a factory whose
        //    parameter list includes `onClick`. We probe by lower-camel-casing
        //    the missing name (OnClick → onClick) and asking the factory index.
        //
        // Receiver-anchoring caveat: today we confirm only that the receiver's
        // STATIC TYPE matches the factory's return type. We do not walk the
        // syntax tree back to prove the receiver expression IS that factory's
        // invocation — so a local `ButtonElement` from elsewhere could still
        // trip this path. Full AST-anchored receiver verification moves to the
        // Phase-3 receiver-anchored rule infrastructure (spec 038 §3.1a),
        // where rules bind through `RuleSymbolResolver` instead of fuzzy
        // factory-index probing.
        var camel = ToCamelCase(memberName);
        if (ctx.Factories.TryFindParameter(camel, out var owner, out var parameter))
        {
            // Confirm the receiver IS-A the factory's return type. We dropped
            // the reverse direction (`returns IS-A receiver`) because it
            // accepted factories returning a more specific type than the
            // receiver, which produces rewrites that don't type-check.
            var returns = owner.Method.ReturnType;
            if (IsAssignableFrom(returns, ctx.Receiver))
            {
                var factoryName = owner.Method.Name;
                var paramsList = string.Join(", ", owner.Method.Parameters.Select(p =>
                    string.Equals(p.Name, camel, StringComparison.Ordinal) ? $"{p.Name}: x" : p.Name));
                var text = $"{factoryName}({paramsList})";
                var evidence = $"factory has {parameter.Type.Name} {camel} parameter";
                return new SuggestionResult(text, 0.9, evidence);
            }
        }

        // 2. Fuzzy match against the receiver's instance members.
        var candidates = CollectInstanceMembers(ctx.Receiver);
        if (candidates.Count == 0) return SuggestionResult.Silent;

        var ranked = RankByJaroWinkler(memberName, candidates, m => m.Name);
        if (ranked.Count == 0) return SuggestionResult.Silent;

        var top = ranked[0];
        if (top.Score < Thresholds.SimilarityFloor) return SuggestionResult.Silent;

        var confidence = ScoreToConfidence(top.Score, ranked, isReactorType: IsReactorType(ctx.Receiver));
        // Per-code emit threshold is applied by Suggest(); SuggestRaw exposes
        // the unfiltered confidence so the tuning harness can sweep T.

        var fullName = $"{ctx.Receiver.Name}.{top.Item.Name}";
        return new SuggestionResult(
            Text: fullName,
            Confidence: confidence,
            Evidence: $"member of {ctx.Receiver.Name}, similarity {top.Score:F2}");
    }

    // ── CS0103 ─────────────────────────────────────────────────────

    static SuggestionResult SuggestForCS0103(in SuggesterContext ctx)
    {
        if (ctx.Node is not IdentifierNameSyntax ident) return SuggestionResult.Silent;
        var name = ident.Identifier.ValueText;
        if (string.IsNullOrEmpty(name)) return SuggestionResult.Silent;

        // Walk Reactor factory names; rank by JaroWinkler. Filter by return-type
        // assignability when we can infer the expected type at the use site.
        if (ctx.Factories.All.Count == 0) return SuggestionResult.Silent;

        var byName = ctx.Factories.ByName.Keys.ToArray();
        var ranked = RankByJaroWinkler(name, byName, n => n);
        if (ranked.Count == 0) return SuggestionResult.Silent;

        var top = ranked[0];
        if (top.Score < Thresholds.SimilarityFloor) return SuggestionResult.Silent;

        var confidence = ScoreToConfidence(top.Score, ranked, isReactorType: true);

        return new SuggestionResult(
            Text: top.Item,
            Confidence: confidence,
            Evidence: $"Reactor factory, similarity {top.Score:F2}");
    }

    // ── CS0117 ─────────────────────────────────────────────────────

    static SuggestionResult SuggestForCS0117(in SuggesterContext ctx)
    {
        if (ctx.Receiver is null || ctx.Node is null) return SuggestionResult.Silent;
        var memberName = ExtractMissingMemberName(ctx.Node);
        if (memberName is null) return SuggestionResult.Silent;

        var candidates = CollectStaticMembers(ctx.Receiver);
        if (candidates.Count == 0) return SuggestionResult.Silent;

        var ranked = RankByJaroWinkler(memberName, candidates, m => m.Name);
        if (ranked.Count == 0) return SuggestionResult.Silent;

        var top = ranked[0];
        if (top.Score < Thresholds.SimilarityFloor) return SuggestionResult.Silent;

        var confidence = ScoreToConfidence(top.Score, ranked, isReactorType: IsReactorType(ctx.Receiver));

        return new SuggestionResult(
            Text: $"{ctx.Receiver.Name}.{top.Item.Name}",
            Confidence: confidence,
            Evidence: $"static member of {ctx.Receiver.Name}, similarity {top.Score:F2}");
    }

    // ── CS1503 ─────────────────────────────────────────────────────

    static SuggestionResult SuggestForCS1503(in SuggesterContext ctx)
    {
        // Special-case Element-expected-vs-string-supplied. We pull the
        // expected type from the diagnostic message via a heuristic: if the
        // syntax position is an argument and the parameter type contains
        // "Element" while the supplied is a string literal, propose Caption /
        // Heading / Body.
        if (ctx.Node is null) return SuggestionResult.Silent;

        var msg = ctx.Diagnostic.GetMessage();
        // CS1503 message: "Argument 1: cannot convert from 'X' to 'Y'"
        // We're looking for ('string', 'Element') and ('Action<T>', 'Action').
        if (msg.Contains("'string'") && (msg.Contains("'Element'") || msg.Contains(".Element'")))
        {
            return new SuggestionResult(
                Text: "Caption(..) | Heading(..) | TextBlock(..)",
                Confidence: 0.8,
                Evidence: "CS1503 expects Element; supplied string — wrap with a text factory");
        }
        if ((msg.Contains("'System.Action<") || msg.Contains("'Action<")) && msg.Contains("'System.Action'"))
        {
            return new SuggestionResult(
                Text: "use a parameterless lambda: () => { ... }",
                Confidence: 0.8,
                Evidence: "CS1503 expects Action; supplied Action<T> — drop the parameter");
        }
        return SuggestionResult.Silent;
    }

    // ── CS7036 ─────────────────────────────────────────────────────

    static SuggestionResult SuggestForCS7036(in SuggesterContext ctx)
    {
        // Diagnostic span often lands on the method name; walk up to the
        // enclosing invocation so we can rank overloads by parameter shape.
        InvocationExpressionSyntax? inv = ctx.Node as InvocationExpressionSyntax;
        for (var n = ctx.Node; inv is null && n is not null; n = n.Parent)
            inv = n as InvocationExpressionSyntax;
        if (inv is null) return SuggestionResult.Silent;
        var calledName = ExtractInvokedName(inv);
        if (calledName is null) return SuggestionResult.Silent;

        if (!ctx.Factories.ByName.TryGetValue(calledName, out var overloads)) return SuggestionResult.Silent;
        if (overloads.Count == 0) return SuggestionResult.Silent;

        var providedArgCount = inv.ArgumentList.Arguments.Count;

        // Pick the overload with the smallest |params| - |provided| difference.
        // Equal-distance overloads keep the first one seen — full Hamming over
        // the (kind, type)-vector is deferred until Data Checkpoint B+ shows a
        // case where shape-matters beyond arity (spec 038 §1.5).
        FactoryOverload? best = null;
        int bestDistance = int.MaxValue;
        foreach (var ov in overloads)
        {
            var d = Math.Abs(ov.Method.Parameters.Length - providedArgCount);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = ov;
            }
        }
        if (best is null) return SuggestionResult.Silent;

        var paramList = string.Join(", ", best.Method.Parameters.Select(p => $"{p.Name}: <{p.Type.Name}>"));
        return new SuggestionResult(
            Text: $"{calledName}({paramList})",
            Confidence: 0.78,
            Evidence: $"closest overload has {best.Method.Parameters.Length} parameters");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    internal static string? ExtractMissingMemberName(SyntaxNode node)
    {
        return node switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            InvocationExpressionSyntax inv => ExtractInvokedName(inv),
            _ => null,
        };
    }

    internal static string? ExtractInvokedName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax id => id.Identifier.ValueText,
        _ => null,
    };

    internal static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (char.IsLower(name[0])) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    static List<ISymbol> CollectInstanceMembers(ITypeSymbol receiver)
    {
        var list = new List<ISymbol>();
        // Walk the receiver and its base types; instance, public.
        for (var t = receiver; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers())
            {
                if (m.IsStatic) continue;
                if (m.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public) continue;
                // Skip synthesized accessors (get_X / set_X / add_X / remove_X)
                // — same hazard as CollectStaticMembers.
                if (m is IMethodSymbol method && method.MethodKind != MethodKind.Ordinary)
                    continue;
                if (m.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event)
                    list.Add(m);
            }
        }
        return list;
    }

    static List<ISymbol> CollectStaticMembers(ITypeSymbol receiver)
    {
        var list = new List<ISymbol>();
        foreach (var m in receiver.GetMembers())
        {
            if (!m.IsStatic) continue;
            if (m.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public) continue;
            // Filter synthesized property accessors (get_X / set_X) so they
            // don't leak into CS0117 suggestions as user-callable members.
            // Spec-038 525-run calibration surfaced ~four conf=0.88 emissions
            // of `Theme.get_Background`; this is the structural fix.
            if (m is IMethodSymbol method && method.MethodKind != MethodKind.Ordinary)
                continue;
            if (m.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field)
                list.Add(m);
        }
        return list;
    }

    internal static bool IsReactorType(ITypeSymbol t)
        => t.ContainingNamespace?.ToDisplayString() is { } ns
            && (ns == "Microsoft.UI.Reactor"
                || ns.StartsWith("Microsoft.UI.Reactor.", StringComparison.Ordinal));

    static bool IsAssignableFrom(ITypeSymbol target, ITypeSymbol source)
    {
        if (SymbolEqualityComparer.Default.Equals(target, source)) return true;
        for (var t = source.BaseType; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(target, t)) return true;
        foreach (var iface in source.AllInterfaces)
            if (SymbolEqualityComparer.Default.Equals(target, iface)) return true;
        return false;
    }

    internal readonly record struct Ranked<T>(T Item, double Score);

    internal static List<Ranked<T>> RankByJaroWinkler<T>(string needle, IReadOnlyCollection<T> haystack, Func<T, string> nameOf)
    {
        var ranked = new List<Ranked<T>>(haystack.Count);
        foreach (var c in haystack)
        {
            var score = StringSimilarity.JaroWinkler(needle, nameOf(c));
            ranked.Add(new Ranked<T>(c, score));
        }
        ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
        return ranked;
    }

    /// <summary>
    /// Spec 038 §5 confidence formula. The base signal is the JaroWinkler
    /// similarity itself (already in [0, 1]); we floor at 0.7, then nudge:
    ///   • Strong margin (≥ 0.2 vs. top-2) → +0.1: a clear winner is a much
    ///     stronger signal than a marginal one.
    ///   • Close tie (&lt; 0.03 vs. top-2) → ×0.6 ambiguity discount.
    ///   • Receiver is a confirmed Reactor type → +0.1.
    /// All adjustments compose; cap at 1.0.
    /// </summary>
    internal static double ScoreToConfidence<T>(double topScore, IReadOnlyList<Ranked<T>> ranked, bool isReactorType)
    {
        if (topScore < Thresholds.SimilarityFloor) return 0.0;
        double conf = Math.Min(1.0, topScore);

        if (ranked.Count >= 2)
        {
            var margin = topScore - ranked[1].Score;
            if (margin < 0.03) conf *= 0.6;
            else if (margin >= 0.2) conf = Math.Min(1.0, conf + 0.1);
        }
        else
        {
            // Only candidate: treat as strong margin.
            conf = Math.Min(1.0, conf + 0.1);
        }

        if (isReactorType) conf = Math.Min(1.0, conf + 0.1);
        return conf;
    }
}
