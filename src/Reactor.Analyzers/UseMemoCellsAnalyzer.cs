using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Implements <c>REACTOR_HOOKS_007</c>: the <c>builder</c> lambda passed to
/// <c>UseMemoCells</c> / <c>UseMemoCellsByKey</c> / <c>UseMemoCellsByIndex</c>
/// closes over a value that is not declared in the trailing <c>params deps</c>
/// list. The cell will not invalidate when that value changes — the cell
/// silently renders stale.
/// </summary>
/// <remarks>
/// <para>
/// Spec 034 §C. The hook intentionally takes a <c>params object[] deps</c>
/// trailing argument list to match <c>UseMemo</c> / <c>UseEffect</c> /
/// <c>UseCallback</c>; this analyzer is what makes the hook safe.
/// </para>
/// <para>
/// <b>Known blind spot:</b> indirect captures through an intermediate method
/// call (<c>builder</c> calls <c>RenderRow(item)</c> which closes over state)
/// are not detected. Same blind spot as React's
/// <c>react-hooks/exhaustive-deps</c>. Documented in the user docs; no static
/// fix available without whole-program analysis.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseMemoCellsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_HOOKS_007";

    /// <summary>
    /// Property bag key carrying the captured symbol's name from the
    /// analyzer to <c>UseMemoCellsCodeFix</c>. Round-tripping through
    /// <see cref="Diagnostic.Properties"/> is more robust than re-parsing
    /// it out of the diagnostic message text — message edits or
    /// localization would otherwise break the codefix silently.
    /// </summary>
    public const string CaptureNameProperty = "CaptureName";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Builder closure captures variable not in dependencies",
        "'{0}' is captured by the builder lambda but missing from the dependencies arg list. The cell will not invalidate when '{0}' changes.",
        "Reactor.Hooks",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "UseMemoCells caches cells by item value plus declared deps. Closure captures missing from deps cause cells to silently render stale when those captured values change.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    private static readonly ImmutableHashSet<string> HookNames =
        ImmutableHashSet.Create("UseMemoCells", "UseMemoCellsByKey", "UseMemoCellsByIndex");

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // <snippet:memo-cells-rule>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var name = GetInvokedMethodName(invocation);
        if (name is null || !HookNames.Contains(name)) return;

        var model = context.SemanticModel;
        var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        // Match by symbol so user-defined methods named UseMemoCells in
        // unrelated namespaces don't trip the analyzer.
        if (!IsReactorMemoCellsHook(symbol)) return;
        // </snippet:memo-cells-rule>

        // Find the `builder` parameter index — it's named `builder` in all three overloads.
        var parameters = symbol.Parameters;
        int builderParamIdx = -1;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Name == "builder")
            {
                builderParamIdx = i;
                break;
            }
        }
        if (builderParamIdx < 0) return;

        var args = invocation.ArgumentList.Arguments;
        var builderArg = ResolveArgumentForParameter(args, parameters, builderParamIdx);
        if (builderArg is null) return;

        // Builder must be a lambda for the analyzer to walk its body.
        switch (builderArg.Expression)
        {
            case SimpleLambdaExpressionSyntax simple:
                {
                    var sym = model.GetSymbolInfo(simple).Symbol as IMethodSymbol;
                    AnalyzeLambda(context, simple, simple.Body, sym?.Parameters, args, parameters);
                    return;
                }
            case ParenthesizedLambdaExpressionSyntax paren:
                {
                    var sym = model.GetSymbolInfo(paren).Symbol as IMethodSymbol;
                    AnalyzeLambda(context, paren, paren.Body, sym?.Parameters, args, parameters);
                    return;
                }
            case AnonymousMethodExpressionSyntax anon:
                {
                    var sym = model.GetSymbolInfo(anon).Symbol as IMethodSymbol;
                    AnalyzeLambda(context, anon, anon.Body, sym?.Parameters, args, parameters);
                    return;
                }
            default:
                // Non-lambda builder (e.g. method group) — captures aren't
                // syntactically visible. Skip without diagnostic; the
                // documented blind spot covers indirect captures anyway.
                return;
        }
    }

    private static void AnalyzeLambda(
        SyntaxNodeAnalysisContext context,
        SyntaxNode lambdaNode,
        SyntaxNode? lambdaBody,
        ImmutableArray<IParameterSymbol>? lambdaParams,
        Microsoft.CodeAnalysis.SeparatedSyntaxList<ArgumentSyntax> args,
        ImmutableArray<IParameterSymbol> parameters)
    {
        if (lambdaBody is null) return;

        var model = context.SemanticModel;

        // Use Roslyn's data-flow analysis to enumerate captures from the lambda.
        DataFlowAnalysis? flow = lambdaBody switch
        {
            ExpressionSyntax expr => model.AnalyzeDataFlow(expr),
            BlockSyntax block => model.AnalyzeDataFlow(block),
            _ => null,
        };
        if (flow is null || !flow.Succeeded) return;

        // Captures = variables read inside the lambda but defined outside it.
        // Roslyn distinguishes "Captured" (closure captures of outer locals/params).
        // For instance-field captures, ReadInside contains the field symbol;
        // we filter to variables defined outside the lambda below.
        var lambdaParamSymbols = lambdaParams is { } lp
            ? new HashSet<ISymbol>(lp.Cast<ISymbol>(), SymbolEqualityComparer.Default)
            : new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        // Build the capture set: real outer variables (Captured), plus
        // member accesses through `this` whose target field/property is
        // read inside the lambda. Roslyn doesn't expose "this captured"
        // directly via DataFlow on FieldSymbol; we walk the lambda body
        // looking for member-access reads on `this`.
        var captures = new List<ISymbol>();
        foreach (var sym in flow.Captured)
        {
            // Skip the lambda's own parameters.
            if (lambdaParamSymbols.Contains(sym)) continue;
            // Skip the implicit `this` capture — accessing instance members
            // captures `this`, but the user-meaningful capture is the
            // field/property/method we walk for separately below.
            if (sym is IParameterSymbol p && p.IsThis) continue;
            // Skip method symbols. A lambda calling `RenderRow(item)` captures
            // `this` (handled above) but the method itself isn't a value the
            // user would be expected to declare in deps.
            if (sym is IMethodSymbol) continue;
            // Skip static-readonly fields and consts — they can't change.
            if (IsImmutableSymbol(sym)) continue;
            captures.Add(sym);
        }

        // Walk the body for direct member-access through `this` (instance
        // fields/properties). DataFlow.Captured doesn't include these for
        // non-async lambdas, but the user clearly is depending on them.
        if (lambdaBody is SyntaxNode bodyNode)
        {
            foreach (var node in bodyNode.DescendantNodes())
            {
                if (node is MemberAccessExpressionSyntax ma)
                {
                    // Pattern: this.X, or implicit `this` with bare member name
                    // (Roslyn resolves bare X to the symbol; we handle that via Identifier below).
                    if (ma.Expression is ThisExpressionSyntax)
                    {
                        var sym = model.GetSymbolInfo(ma).Symbol;
                        if (sym is IFieldSymbol or IPropertySymbol && !IsImmutableSymbol(sym))
                        {
                            if (!captures.Any(c => SymbolEqualityComparer.Default.Equals(c, sym)))
                                captures.Add(sym);
                        }
                    }
                }
                else if (node is IdentifierNameSyntax id)
                {
                    // Skip identifiers that are part of a member access (already handled above).
                    if (id.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == id) continue;
                    var sym = model.GetSymbolInfo(id).Symbol;
                    if (sym is null) continue;
                    // Implicit-this instance field / property access.
                    if ((sym is IFieldSymbol fld && !fld.IsStatic) ||
                        (sym is IPropertySymbol prop && !prop.IsStatic))
                    {
                        if (IsImmutableSymbol(sym)) continue;
                        if (!captures.Any(c => SymbolEqualityComparer.Default.Equals(c, sym)))
                            captures.Add(sym);
                    }
                }
            }
        }

        if (captures.Count == 0) return;

        // Resolve the symbols listed in the trailing `params` deps slot.
        var depSymbols = ResolveDepSymbols(model, args, parameters);

        foreach (var capture in captures)
        {
            if (depSymbols.Any(d => SymbolEqualityComparer.Default.Equals(d, capture))) continue;

            // Report on the call-site location so the warning lands near
            // the hook invocation, not deep in the lambda body.
            var props = ImmutableDictionary<string, string?>.Empty
                .Add(CaptureNameProperty, capture.Name);
            var diag = Diagnostic.Create(Rule, lambdaNode.GetLocation(), props, capture.Name);
            context.ReportDiagnostic(diag);
        }
    }

    private static bool IsImmutableSymbol(ISymbol sym)
    {
        switch (sym)
        {
            case IFieldSymbol field:
                if (field.IsConst) return true;
                if (field.IsStatic && field.IsReadOnly) return true;
                return false;
            case ILocalSymbol local:
                if (local.IsConst) return true;
                return false;
        }
        return false;
    }

    private static List<ISymbol> ResolveDepSymbols(
        SemanticModel model,
        Microsoft.CodeAnalysis.SeparatedSyntaxList<ArgumentSyntax> args,
        ImmutableArray<IParameterSymbol> parameters)
    {
        var result = new List<ISymbol>();

        if (parameters.Length == 0) return result;
        var last = parameters[parameters.Length - 1];
        if (!(last.IsParams && (last.Name is "deps" or "dependencies"))) return result;

        int fixedArity = parameters.Length - 1;

        // Path 1: trailing positional args after fixedArity belong to the
        // params tail. `UseMemoCells(items, builder, theme, selection)`.
        for (int i = fixedArity; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.NameColon is not null) continue;
            var depSym = ResolveSymbolFromExpression(model, arg.Expression);
            if (depSym is not null) result.Add(depSym);
        }

        // Path 2: a single explicit array passed for the params slot.
        if (args.Count == fixedArity + 1)
        {
            var lastArg = args[fixedArity];
            switch (lastArg.Expression)
            {
                case ArrayCreationExpressionSyntax arr when arr.Initializer is { } init:
                    foreach (var el in init.Expressions)
                    {
                        var s = ResolveSymbolFromExpression(model, el);
                        if (s is not null && !result.Any(r => SymbolEqualityComparer.Default.Equals(r, s)))
                            result.Add(s);
                    }
                    break;
                case ImplicitArrayCreationExpressionSyntax implArr:
                    foreach (var el in implArr.Initializer.Expressions)
                    {
                        var s = ResolveSymbolFromExpression(model, el);
                        if (s is not null && !result.Any(r => SymbolEqualityComparer.Default.Equals(r, s)))
                            result.Add(s);
                    }
                    break;
                case CollectionExpressionSyntax coll:
                    foreach (var el in coll.Elements)
                    {
                        if (el is ExpressionElementSyntax exprEl)
                        {
                            var s = ResolveSymbolFromExpression(model, exprEl.Expression);
                            if (s is not null && !result.Any(r => SymbolEqualityComparer.Default.Equals(r, s)))
                                result.Add(s);
                        }
                    }
                    break;
            }
        }

        return result;
    }

    private static ISymbol? ResolveSymbolFromExpression(SemanticModel model, ExpressionSyntax expr)
    {
        // Match captures by symbol so `this.theme` and bare `theme` resolve
        // to the same field/property. For local variables and parameters, the
        // identifier resolves to ILocalSymbol / IParameterSymbol — same case.
        var inner = UnwrapCasts(expr);
        return model.GetSymbolInfo(inner).Symbol;
    }

    private static ExpressionSyntax UnwrapCasts(ExpressionSyntax expr)
    {
        while (true)
        {
            switch (expr)
            {
                case CastExpressionSyntax c: expr = c.Expression; continue;
                case ParenthesizedExpressionSyntax p: expr = p.Expression; continue;
                default: return expr;
            }
        }
    }

    private static ArgumentSyntax? ResolveArgumentForParameter(
        Microsoft.CodeAnalysis.SeparatedSyntaxList<ArgumentSyntax> args,
        ImmutableArray<IParameterSymbol> parameters,
        int paramIndex)
    {
        // Named arg targeting `builder`?
        foreach (var a in args)
        {
            if (a.NameColon is { } nc && nc.Name.Identifier.Text == parameters[paramIndex].Name)
                return a;
        }
        // Positional.
        if (paramIndex < args.Count) return args[paramIndex];
        return null;
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax gn => gn.Identifier.Text,
            _ => null,
        };
    }

    private static bool IsReactorMemoCellsHook(IMethodSymbol symbol)
    {
        // Match the exact extension class the hook lives in. Both
        // RenderContext and Component shim sets are accepted.
        var container = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "");
        return container is
            "Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions"
            or "Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions";
    }
}
