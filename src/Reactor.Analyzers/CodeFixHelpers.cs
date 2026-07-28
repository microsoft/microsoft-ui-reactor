using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Shared helpers for the allocation-family code fixes (HOOKS_013, CTX_001) that wrap a value in
/// <c>UseMemo(() =&gt; …, [])</c>.
/// </summary>
internal static class CodeFixHelpers
{
    /// <summary>
    /// Returns the type-argument text to place on the emitted <c>UseMemo</c> call so the wrapped
    /// expression still compiles:
    /// <list type="bullet">
    /// <item><description><c>""</c> — the expression carries its own type (explicit <c>new T()</c>,
    ///   typed array, …); a bare <c>UseMemo(() =&gt; expr, [])</c> infers fine.</description></item>
    /// <item><description><c>"&lt;global::…&gt;"</c> — the expression is <b>target-typed</b>
    ///   (<c>new()</c> / a collection expression), which loses its type inside the untyped lambda
    ///   <c>() =&gt; expr</c>; an explicit <c>UseMemo&lt;T&gt;</c> restores it.</description></item>
    /// <item><description><c>null</c> — target-typed but the type could not be resolved; the caller
    ///   should withhold the fix rather than emit code that would not compile.</description></item>
    /// </list>
    /// </summary>
    public static string? UseMemoTypeArgument(ExpressionSyntax expr, SemanticModel model, CancellationToken ct)
    {
        var unwrapped = expr;
        while (true)
        {
            switch (unwrapped)
            {
                case ParenthesizedExpressionSyntax paren: unwrapped = paren.Expression; continue;
                case CastExpressionSyntax cast: unwrapped = cast.Expression; continue;
            }
            break;
        }

        if (unwrapped is not (ImplicitObjectCreationExpressionSyntax or CollectionExpressionSyntax))
            return "";

        var typeInfo = model.GetTypeInfo(expr, ct);
        var type = typeInfo.Type ?? typeInfo.ConvertedType;
        if (type is null || type.TypeKind == TypeKind.Error)
            return null;

        return "<" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">";
    }
}
