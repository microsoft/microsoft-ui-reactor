using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_CTX_001</c> — flags <c>element.Provide(ctx, value)</c> where <paramref name="value"/>
/// is a freshly-allocated object/array/collection whose type compares by <b>reference</b>.
/// </summary>
/// <remarks>
/// Context values are diffed with <c>ContextValuesEqual</c>, which compares each value via
/// <c>object.Equals</c> (<c>Element.cs:1358</c>) — <b>not</b> reference identity. A freshly
/// allocated <c>record</c>/<c>struct</c> (or a class overriding <c>Equals(object)</c>) with
/// unchanged fields therefore compares <b>equal</b> and does not thrash consumers, so it must NOT
/// fire. A reference type that uses reference equality under <c>object.Equals</c> — a plain class,
/// an array, a mutable collection, <b>or a bare <c>IEquatable&lt;T&gt;</c> that does not also
/// override <c>Equals(object)</c></b> (which falls back to reference equality here) — re-allocated
/// each render compares unequal and forces every <c>UseContext</c> consumer in the subtree to
/// re-render. Because that is an allocation/perf nudge (not a correctness bug), the rule ships at
/// <see cref="DiagnosticSeverity.Info"/>.
/// <para>
/// The value-equality check is <b>mandatory</b>: without it the rule is wrong (this was a blocking
/// error in the spec's first draft). <c>Provide</c> is the extension method
/// <c>ContextExtensions.Provide&lt;T,TValue&gt;</c> (<c>ContextExtensions.cs:11</c>).
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContextProvideAnalyzer : DiagnosticAnalyzer
{
    public const string Id = "REACTOR_CTX_001";

    private static readonly DiagnosticDescriptor Rule = new(
        Id,
        "Context value re-allocated each render",
        "This context value is a freshly-allocated {0} whose type compares by reference, so it differs from last render's value and re-renders every UseContext consumer in the subtree. Memoize it (UseMemo(() => …, deps)) or provide a value-equality type (a record).",
        "Reactor.Context",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Context values are diffed with object.Equals (Element.ContextValuesEqual), not reference identity. A reference type that uses reference equality under object.Equals (a plain class / array / mutable collection, or a bare IEquatable<T> that does not also override Equals(object)) allocated fresh each render compares unequal and thrashes every consumer. Wrap it in UseMemo so the same instance is reused across renders. Records, structs, and classes overriding Equals(object) compare by value and do not fire.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Syntactic gate: a member-access call named Provide.
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "Provide" }) return;

        var model = context.SemanticModel;
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol symbol) return;

        // Confirm it is the Reactor ContextExtensions.Provide extension method.
        if (symbol.Name != "Provide") return;
        if (symbol.ContainingType?.Name != "ContextExtensions") return;
        if (!CommandDebounceAnalyzer.IsReactorNamespace(symbol.ContainingNamespace?.ToDisplayString())) return;

        // Bind the `value` argument by parameter name (reduced-extension form has parameters
        // [context, value]; the unreduced static form has [element, context, value]).
        var valueExpr = FindValueArgument(invocation, symbol);
        if (valueExpr is null) return;

        // Must be a fresh allocation (restricted, with-aware classifier).
        var (unstable, kind) = AllocationAnalysis.ClassifyRestricted(valueExpr);
        if (!unstable) return;

        // MANDATORY: only reference-equality types thrash consumers. Records / structs /
        // Equals(object)-overriders compare by value and must not fire. Context values are diffed
        // with object.Equals (Element.ContextValuesEqual), so a bare IEquatable<T> without an
        // Equals(object) override falls back to reference equality and DOES fire. Fall back to
        // ConvertedType so target-typed forms (e.g. a `[]` collection expression) still resolve.
        var type = model.GetTypeInfo(valueExpr).Type ?? model.GetTypeInfo(valueExpr).ConvertedType;
        if (type is null || AllocationAnalysis.HasValueEquality(type, objectEqualsSemantics: true)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, valueExpr.GetLocation(), kind));
    }

    private static ExpressionSyntax? FindValueArgument(InvocationExpressionSyntax invocation, IMethodSymbol symbol)
    {
        var args = invocation.ArgumentList.Arguments;
        // Reduced extension call: the receiver is the `this` element, so the argument list is
        // [context, value] and `symbol.Parameters` is [context, value].
        var parameters = symbol.Parameters;
        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            IParameterSymbol? param = null;
            if (arg.NameColon is not null)
                param = parameters.FirstOrDefault(p => p.Name == arg.NameColon.Name.Identifier.Text);
            else if (i < parameters.Length)
                param = parameters[i];

            if (param is not null && param.Name == "value")
                return arg.Expression;
        }
        return null;
    }
}
