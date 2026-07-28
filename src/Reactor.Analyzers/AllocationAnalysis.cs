using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Shared, extract-first helpers for the spec-060 "HOOKS_004 allocation family"
/// (<c>REACTOR_HOOKS_012</c>, <c>REACTOR_HOOKS_013</c>, <c>REACTOR_CTX_001</c>).
/// </summary>
/// <remarks>
/// Two concerns live here so all three consumers judge them identically:
/// <list type="bullet">
/// <item><description><see cref="ClassifyRestricted"/> — a <b>restricted</b>, <c>with</c>-aware
///   variant of <c>HookRulesAnalyzer.ClassifyDepExpression</c>. The raw HOOKS_004 helper also
///   treats <c>TupleExpression</c>, lambdas, and anonymous methods as "unstable" (correct for a
///   <c>deps</c> array, where any fresh reference breaks equality). The allocation family needs a
///   narrower set: an expression that <b>heap-allocates a fresh value every render</b> —
///   object / array / anonymous-object / collection creation, plus <c>value with { … }</c>. A
///   stack <c>ValueTuple</c> (<c>(0, "")</c>) and a deliberately-stored lambda are <b>not</b>
///   allocations of the kind these rules target, so they are excluded (spec §3.2 / §4.1
///   HOOKS_013 accuracy note).</description></item>
/// <item><description><see cref="HasValueEquality"/> — does a type compare by value? Records,
///   value types, <c>IEquatable&lt;T&gt;</c> implementers, and classes overriding
///   <c>Equals(object)</c> do. This is the mandatory gate that keeps HOOKS_012 / CTX_001 from
///   firing on a freshly-allocated record/struct, which the context/deps diff (<c>Equals</c>)
///   compares <b>equal</b> and does not thrash (spec §4.1 CTX_001 correction — this was a
///   blocking error in the first draft).</description></item>
/// </list>
/// </remarks>
internal static class AllocationAnalysis
{
    /// <summary>
    /// Classifies <paramref name="expr"/> as a fresh per-render heap allocation of the kind the
    /// allocation-family rules target. Returns <c>(true, kind)</c> for object / array /
    /// anonymous-object / collection creation and <c>value with { … }</c>; <c>(false, "")</c>
    /// otherwise. Unlike the raw HOOKS_004 classifier it deliberately EXCLUDES tuple expressions,
    /// lambdas, and anonymous methods.
    /// </summary>
    public static (bool Unstable, string Kind) ClassifyRestricted(ExpressionSyntax expr)
    {
        expr = Unwrap(expr);

        return expr switch
        {
            ObjectCreationExpressionSyntax => (true, "object"),
            ImplicitObjectCreationExpressionSyntax => (true, "object"),
            ArrayCreationExpressionSyntax => (true, "array"),
            ImplicitArrayCreationExpressionSyntax => (true, "array"),
            CollectionExpressionSyntax => (true, "collection"),
            AnonymousObjectCreationExpressionSyntax => (true, "anonymous object"),
            // `value with { … }` copies the value, so a reference-type record allocates a fresh
            // instance each render exactly like `new …`. Recognising it closes the known
            // false-negative called out in spec §3.2 / §4.1 (CTX_001/HOOKS_013). The value-equality
            // gate (HOOKS_012 / CTX_001) and HOOKS_013's own value-type skip both filter out
            // record-struct `with` results, which are stack values and do not heap-allocate.
            WithExpressionSyntax => (true, "object"),
            _ => (false, ""),
        };
    }

    /// <summary>
    /// True when values of <paramref name="type"/> compare by <b>value</b>. Two equality models:
    /// <list type="bullet">
    /// <item><description>Default (<paramref name="objectEqualsSemantics"/> = false) — matches
    ///   <c>EqualityComparer&lt;T&gt;.Default</c> (the UseState/UsePersisted setter, HOOKS_010):
    ///   value types, records, <c>IEquatable&lt;T&gt;</c> implementers, and <c>Equals(object)</c>
    ///   overriders all count.</description></item>
    /// <item><description><paramref name="objectEqualsSemantics"/> = true — matches
    ///   <c>object.Equals</c> (memo-deps <c>Reconciler.DepsEqual</c> and context
    ///   <c>Element.ContextValuesEqual</c>, used by HOOKS_012 / CTX_001): a bare
    ///   <c>IEquatable&lt;T&gt;</c> implementer that does <b>not</b> override <c>Equals(object)</c>
    ///   falls back to <b>reference</b> equality at runtime, so it does NOT count. Only value types,
    ///   records, and <c>Equals(object)</c> overriders do.</description></item>
    /// </list>
    /// Reference types with none of these (a plain class, an array, <c>List&lt;T&gt;</c>) return false.
    /// </summary>
    public static bool HasValueEquality(ITypeSymbol? type, bool objectEqualsSemantics = false)
    {
        if (type is null)
            return false;

        // Structs (incl. record struct, tuples, primitives) compare by value — object.Equals on a
        // value type uses ValueType's value-wise Equals, so both models agree.
        if (type.IsValueType)
            return true;

        // Arrays are reference-equality only.
        if (type is IArrayTypeSymbol)
            return false;

        if (type is not INamedTypeSymbol named)
            return false;

        // `record class` synthesises a value-based Equals(object)/== — value equality under both models.
        if (named.IsRecord)
            return true;

        // Overrides object.Equals(object) anywhere in the hierarchy (below System.Object) — value
        // equality under both models.
        if (OverridesObjectEquals(named))
            return true;

        // IEquatable<self> WITHOUT an Equals(object) override is value equality ONLY under
        // EqualityComparer<T>.Default. Under object.Equals it is ignored (reference fallback), so
        // callers using object.Equals (HOOKS_012 / CTX_001) must not treat it as value equality.
        if (!objectEqualsSemantics && ImplementsIEquatableOfSelf(named))
            return true;

        return false;
    }

    private static bool ImplementsIEquatableOfSelf(INamedTypeSymbol named)
    {
        foreach (var iface in named.AllInterfaces)
        {
            // Match System.IEquatable<self> specifically — a user-defined interface that happens to
            // be named IEquatable in another namespace must not count as value equality.
            if (iface.Name == "IEquatable"
                && iface.TypeArguments.Length == 1
                && iface.ContainingNamespace is { Name: "System" } ns
                && ns.ContainingNamespace is { IsGlobalNamespace: true }
                && SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], named))
            {
                return true;
            }
        }
        return false;
    }

    private static bool OverridesObjectEquals(INamedTypeSymbol type)
    {
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers("Equals").OfType<IMethodSymbol>())
            {
                if (member.IsOverride
                    && member.Parameters.Length == 1
                    && member.Parameters[0].Type.SpecialType == SpecialType.System_Object)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Peels casts and parentheses so the classifier sees the underlying creation.</summary>
    private static ExpressionSyntax Unwrap(ExpressionSyntax expr)
    {
        while (true)
        {
            switch (expr)
            {
                case CastExpressionSyntax cast: expr = cast.Expression; continue;
                case ParenthesizedExpressionSyntax paren: expr = paren.Expression; continue;
                default: return expr;
            }
        }
    }
}
