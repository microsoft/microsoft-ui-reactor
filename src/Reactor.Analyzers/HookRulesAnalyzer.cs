using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// Analyzer implementing the hook-call rules from
/// <c>docs/specs/020-async-resources-design.md</c> §16 Phase 4:
///
/// <list type="bullet">
/// <item><description><c>REACTOR_HOOKS_001</c> — a hook is called conditionally (inside
///   <c>if</c>, <c>for</c>, <c>while</c>, <c>switch</c>, <c>try</c>, or similar). Hook
///   slots are positional; skipping one on any render desynchronizes the entire slot
///   list and corrupts component state.</description></item>
/// <item><description><c>REACTOR_HOOKS_004</c> — a hook's <c>deps</c> argument is a
///   freshly-allocated object, array, or lambda. A new instance every render compares
///   unequal, so the hook never hits its stable path.</description></item>
/// <item><description><c>REACTOR_HOOKS_005</c> — a hook is called outside a
///   <c>Component.Render</c> override or a custom-hook method (by convention, a method
///   whose name starts with <c>Use</c>). Hooks read and write slot state that only
///   exists during a render pass.</description></item>
/// <item><description><c>REACTOR_HOOKS_006</c> — <c>UseResource</c>/<c>UseInfiniteResource</c>
///   is called with a fetcher that names a non-idempotent operation
///   (<c>Post</c>, <c>Create</c>, <c>Delete</c>, <c>GenerateRandom</c>, …). Resources
///   re-run on deps change, retry, and focus revalidation — use <c>UseMutation</c> for
///   writes. This is a name-based heuristic and is <see cref="DiagnosticSeverity.Info"/>.
///   </description></item>
/// </list>
///
/// See <c>docs/specs/tasks/async-resources-implementation.md</c> §4.3 for the full
/// rule catalog. <c>REACTOR_HOOKS_002</c> and <c>003</c> require control-flow /
/// data-flow analysis and are tracked as follow-ups.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HookRulesAnalyzer : DiagnosticAnalyzer
{
    public const string ConditionalHookId = "REACTOR_HOOKS_001";
    public const string UnstableDepsId = "REACTOR_HOOKS_004";
    public const string HookOutsideRenderId = "REACTOR_HOOKS_005";
    public const string NonIdempotentFetcherId = "REACTOR_HOOKS_006";

    private static readonly DiagnosticDescriptor ConditionalHookRule = new(
        ConditionalHookId,
        "Hook called conditionally",
        "Hook '{0}' is called inside a {1}. Hooks must be called unconditionally in the same order on every render.",
        "Reactor.Hooks",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Hook slots are positional. Calling a hook inside a conditional branch desynchronizes the slot list on later renders and silently corrupts state.");

    private static readonly DiagnosticDescriptor UnstableDepsRule = new(
        UnstableDepsId,
        "Hook deps contains freshly allocated value",
        "Hook '{0}' receives a freshly-allocated {1} in its deps. This compares unequal on every render, so the hook never hits its stable path. Memoize with UseMemo, hoist to a field, or project to a scalar key.",
        "Reactor.Hooks",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A new object/array/lambda allocated each render will never match its previous value by default equality, so the hook refetches/reruns every render.");

    private static readonly DiagnosticDescriptor HookOutsideRenderRule = new(
        HookOutsideRenderId,
        "Hook called outside Render or a custom-hook method",
        "Hook '{0}' must be called inside a Component.Render override or a custom-hook method (by convention, a method whose name starts with 'Use')",
        "Reactor.Hooks",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Hooks read and write slot state that only exists during a render pass. Calling one from an event handler, a constructor, or a non-hook helper throws at runtime.");

    private static readonly DiagnosticDescriptor NonIdempotentFetcherRule = new(
        NonIdempotentFetcherId,
        "Resource fetcher looks non-idempotent",
        "Hook '{0}' fetcher references '{1}', which looks like a write. Resources re-run on deps change, retry, and focus revalidation — use UseMutation for writes.",
        "Reactor.Hooks",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "UseResource / UseInfiniteResource fetchers must be idempotent reads. A fetcher named Post/Create/Delete/Generate/... can execute multiple times per user action as the cache restarts on deps change or stale revalidation.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ConditionalHookRule, UnstableDepsRule, HookOutsideRenderRule, NonIdempotentFetcherRule);

    // Hooks that take a `deps` params-array. Only these are candidates for
    // REACTOR_HOOKS_004. For params-arrays, we skip the check if the caller
    // passed zero or one primitive arguments (e.g. `UseEffect(..., myScalar)`).
    private static readonly ImmutableHashSet<string> DepsHooks =
        ImmutableHashSet.Create(
            "UseEffect",
            "UseMemo",
            "UseCallback",
            "UseResource",
            "UseInfiniteResource",
            "UseDataSource");

    // Hooks that take a read-only fetcher. REACTOR_HOOKS_006 walks the fetcher
    // argument and flags invocations whose names look non-idempotent.
    // UseMutation is intentionally excluded — mutations are allowed to write.
    private static readonly ImmutableHashSet<string> FetcherHooks =
        ImmutableHashSet.Create(
            "UseResource",
            "UseInfiniteResource",
            "UseDataSource");

    // Name prefixes that suggest a write or non-deterministic operation. Anchored
    // at word start: a fetcher named `GetPosts` is fine; `PostMessage` is not.
    // Match is case-sensitive on the first letter to respect the usual .NET
    // PascalCase convention (so `postalCode` locals don't collide).
    private static readonly ImmutableArray<string> NonIdempotentPrefixes =
        ImmutableArray.Create(
            "Post",
            "Put",
            "Patch",
            "Delete",
            "Remove",
            "Create",
            "Insert",
            "Update",
            "Save",
            "Send",
            "Publish",
            "Generate",
            "Register",
            "Upsert");

    // <snippet:hook-rules-shape>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var methodName = GetInvokedMethodName(invocation);
        if (methodName is null) return;
        if (!LooksLikeHook(methodName)) return;
        if (!IsLikelyReactorHook(context, invocation)) return;
        // </snippet:hook-rules-shape>

        // REACTOR_HOOKS_005: must be called from a Render() override or a Use* method.
        var enclosing = FindEnclosingMethod(invocation);
        if (!IsRenderOrCustomHook(enclosing))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                HookOutsideRenderRule,
                invocation.GetLocation(),
                methodName));
            // Still useful to report the other two even from a bad location, but the
            // conditional-hook walk anchors on the enclosing method body, so skip it here.
            return;
        }

        // REACTOR_HOOKS_001: conditional hook.
        if (FindConditionalAncestor(invocation, enclosing!) is { } kind)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ConditionalHookRule,
                invocation.GetLocation(),
                methodName,
                kind));
        }

        // REACTOR_HOOKS_004: freshly-allocated deps.
        if (DepsHooks.Contains(methodName))
        {
            CheckDepsArguments(context, invocation, methodName);
        }

        // REACTOR_HOOKS_006: non-idempotent fetcher name.
        if (FetcherHooks.Contains(methodName))
        {
            CheckFetcherArgument(context, invocation, methodName);
        }
    }

    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────

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

    private static bool LooksLikeHook(string name)
        => name.Length > 3 && name.StartsWith("Use") && char.IsUpper(name[3]);

    /// <summary>
    /// Anchor the analysis to Reactor hooks only. Heuristic: the call-site must either
    /// live inside a type that derives from <c>Component</c> / <c>RenderContext</c>,
    /// or be an extension method on <c>RenderContext</c>. This skips unrelated Use*
    /// methods (e.g. <c>WebApplicationBuilder.UseAuthentication</c>) without requiring
    /// a type-sensitive binding step.
    /// </summary>
    private static bool IsLikelyReactorHook(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        var model = context.SemanticModel;
        var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

        // Extension on RenderContext (the static case).
        if (symbol is { IsExtensionMethod: true } && symbol.ReceiverType is INamedTypeSymbol rt
            && IsOrDerivesFrom(rt, "Microsoft.UI.Reactor.Core.RenderContext"))
        {
            return true;
        }

        // Method on Component<>/Component (the protected helpers).
        if (symbol is { ContainingType: INamedTypeSymbol container }
            && IsOrDerivesFrom(container, "Microsoft.UI.Reactor.Core.Component"))
        {
            return true;
        }

        // Fallback — unbound or overload-ambiguous call. Check the receiver expression's
        // declared type, if any. Keeps the analyzer helpful during incremental compile
        // even when symbol resolution is still catching up.
        if (invocation.Expression is MemberAccessExpressionSyntax ma)
        {
            var recType = model.GetTypeInfo(ma.Expression).Type;
            if (recType is INamedTypeSymbol nt && (
                IsOrDerivesFrom(nt, "Microsoft.UI.Reactor.Core.RenderContext") ||
                IsOrDerivesFrom(nt, "Microsoft.UI.Reactor.Core.Component")))
            {
                return true;
            }
        }

        // Implicit receiver (`UseState(...)` inside a Component) — same path as the
        // Component-container check above, but the symbol may be null when the build is
        // mid-type-binding. Check the enclosing class.
        var enclosingType = invocation.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (enclosingType is not null && symbol is null)
        {
            var classSymbol = model.GetDeclaredSymbol(enclosingType) as INamedTypeSymbol;
            if (classSymbol is not null && IsOrDerivesFrom(classSymbol, "Microsoft.UI.Reactor.Core.Component"))
                return true;
        }

        return false;
    }

    private static bool IsOrDerivesFrom(INamedTypeSymbol? type, string fullyQualifiedName)
    {
        while (type is not null)
        {
            var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", "");
            if (name == fullyQualifiedName) return true;
            // Also accept generic forms: Component<T> derives from Component.
            if (name.StartsWith(fullyQualifiedName + "<")) return true;
            type = type.BaseType;
        }
        return false;
    }

    private static MethodDeclarationSyntax? FindEnclosingMethod(SyntaxNode node)
        => node.FirstAncestorOrSelf<MethodDeclarationSyntax>();

    private static bool IsRenderOrCustomHook(MethodDeclarationSyntax? method)
    {
        if (method is null) return false;
        var name = method.Identifier.Text;
        if (name == "Render") return true;
        // Custom-hook convention: method named UseXxx. Matches the existing UseAnnounce,
        // UseValidationContext pattern in the codebase.
        return LooksLikeHook(name);
    }

    /// <summary>
    /// Walks ancestors from the invocation up to (but not past) the enclosing method's body.
    /// If any ancestor is a conditional/loop/try construct, returns its human-readable name.
    /// </summary>
    private static string? FindConditionalAncestor(InvocationExpressionSyntax invocation, MethodDeclarationSyntax boundary)
    {
        for (var node = (SyntaxNode?)invocation.Parent; node is not null && node != boundary; node = node.Parent)
        {
            switch (node)
            {
                case IfStatementSyntax: return "if";
                case ElseClauseSyntax: return "else";
                case ForStatementSyntax: return "for loop";
                case ForEachStatementSyntax: return "foreach loop";
                case WhileStatementSyntax: return "while loop";
                case DoStatementSyntax: return "do-while loop";
                case SwitchStatementSyntax: return "switch";
                case SwitchSectionSyntax: return "switch case";
                case CaseSwitchLabelSyntax: return "switch label";
                case TryStatementSyntax: return "try block";
                case CatchClauseSyntax: return "catch clause";
                case FinallyClauseSyntax: return "finally clause";
                case ConditionalExpressionSyntax: return "conditional expression";
                // Lambdas and local functions are also problematic: the hook only runs
                // when the lambda is invoked, not on every render.
                case SimpleLambdaExpressionSyntax:
                case ParenthesizedLambdaExpressionSyntax:
                case AnonymousMethodExpressionSyntax:
                case LocalFunctionStatementSyntax:
                    return "nested lambda/local function";
            }
        }
        return null;
    }

    private static void CheckDepsArguments(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, string methodName)
    {
        var model = context.SemanticModel;
        var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var args = invocation.ArgumentList.Arguments;
        var parameters = symbol.Parameters;

        // Walk arguments pairing them with parameters (handles positional + named).
        for (int i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            IParameterSymbol? param = null;

            if (arg.NameColon is not null)
            {
                var named = arg.NameColon.Name.Identifier.Text;
                param = parameters.FirstOrDefault(p => p.Name == named);
            }
            else if (i < parameters.Length)
            {
                param = parameters[i];
            }

            if (param is null) continue;
            // Skip unless this is the "deps" parameter (conventionally named). Several
            // overloads exist — names include "deps", "dependencies".
            if (param.Name is not ("deps" or "dependencies")) continue;
            // Params arrays are handled by the tail-pass below; skip here to avoid
            // double-reporting when the user passes a scalar to a `params object[] deps`
            // parameter.
            if (param.IsParams) continue;

            // If the deps parameter is an explicit `object[]`, the argument will be a
            // single array-creation expression (explicit or implicit). Flag the array
            // elements individually.
            if (arg.Expression is ArrayCreationExpressionSyntax arrCreation
                && arrCreation.Initializer is { } init)
            {
                foreach (var el in init.Expressions) CheckSingleDepExpression(context, el, methodName);
            }
            else if (arg.Expression is ImplicitArrayCreationExpressionSyntax implArr)
            {
                foreach (var el in implArr.Initializer.Expressions) CheckSingleDepExpression(context, el, methodName);
            }
            else if (arg.Expression is CollectionExpressionSyntax collExpr)
            {
                foreach (var el in collExpr.Elements)
                {
                    if (el is ExpressionElementSyntax exprEl)
                        CheckSingleDepExpression(context, exprEl.Expression, methodName);
                }
            }
            else
            {
                // Single scalar / reference passed. For params-arrays this is the happy
                // path; check the one expression.
                CheckSingleDepExpression(context, arg.Expression, methodName);
            }
        }

        // Also handle the `params object[] dependencies` tail case: arguments past the
        // last named parameter belong to the params array. For hooks like
        // `UseEffect(Action, params object[] dependencies)` the user writes
        // `UseEffect(() => { ... }, myCallback)` — each trailing arg is one dep.
        if (parameters.Length > 0
            && parameters[parameters.Length - 1] is { IsParams: true } paramsParam
            && (paramsParam.Name is "deps" or "dependencies"))
        {
            int fixedArity = parameters.Length - 1;
            for (int i = fixedArity; i < args.Count; i++)
            {
                // Named args targeting the params tail are unusual; skip.
                if (args[i].NameColon is not null) continue;
                CheckSingleDepExpression(context, args[i].Expression, methodName);
            }
        }
    }

    private static void CheckSingleDepExpression(SyntaxNodeAnalysisContext context, ExpressionSyntax expr, string methodName)
    {
        var (unstable, kind) = ClassifyDepExpression(expr);
        if (!unstable) return;

        context.ReportDiagnostic(Diagnostic.Create(
            UnstableDepsRule,
            expr.GetLocation(),
            methodName,
            kind));
    }

    private static (bool Unstable, string Kind) ClassifyDepExpression(ExpressionSyntax expr)
    {
        // Unwrap a few noise layers.
        expr = UnwrapCasts(expr);

        return expr switch
        {
            ObjectCreationExpressionSyntax => (true, "object"),
            ImplicitObjectCreationExpressionSyntax => (true, "object"),
            ArrayCreationExpressionSyntax => (true, "array"),
            ImplicitArrayCreationExpressionSyntax => (true, "array"),
            CollectionExpressionSyntax => (true, "collection"),
            AnonymousObjectCreationExpressionSyntax => (true, "anonymous object"),
            SimpleLambdaExpressionSyntax => (true, "lambda"),
            ParenthesizedLambdaExpressionSyntax => (true, "lambda"),
            AnonymousMethodExpressionSyntax => (true, "anonymous method"),
            TupleExpressionSyntax => (true, "tuple"),
            _ => (false, ""),
        };
    }

    private static ExpressionSyntax UnwrapCasts(ExpressionSyntax expr)
    {
        while (true)
        {
            switch (expr)
            {
                case CastExpressionSyntax cast: expr = cast.Expression; continue;
                case ParenthesizedExpressionSyntax p: expr = p.Expression; continue;
                default: return expr;
            }
        }
    }

    // ────────────────────────────────────────────────────────────
    // REACTOR_HOOKS_006 helpers
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Locates the fetcher argument for a UseResource-family call and scans it for
    /// invocation names that match the non-idempotent-prefix list. Lambdas have their
    /// body walked; method references are checked by name.
    /// </summary>
    private static void CheckFetcherArgument(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, string methodName)
    {
        var model = context.SemanticModel;
        var symbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0) return;

        // Find the fetcher arg. By convention the first positional arg is the fetcher
        // (`fetcher` for UseResource, `fetchPage` for UseInfiniteResource, `request`
        // for UseDataSource takes the Request as second). Prefer binding to a parameter
        // with a matching name; fall back to position 0 when the symbol is unresolved
        // (mid-compile scenarios).
        ExpressionSyntax? fetcherExpr = null;
        if (symbol is not null)
        {
            var parameters = symbol.Parameters;
            for (int i = 0; i < args.Count; i++)
            {
                var arg = args[i];
                IParameterSymbol? param = null;
                if (arg.NameColon is not null)
                {
                    var named = arg.NameColon.Name.Identifier.Text;
                    param = parameters.FirstOrDefault(p => p.Name == named);
                }
                else if (i < parameters.Length)
                {
                    param = parameters[i];
                }
                if (param is null) continue;
                if (param.Name is "fetcher" or "fetchPage" or "fetch")
                {
                    fetcherExpr = arg.Expression;
                    break;
                }
            }
        }
        // Heuristic fallback: first arg that's a lambda or method reference.
        if (fetcherExpr is null)
        {
            foreach (var arg in args)
            {
                var ex = UnwrapCasts(arg.Expression);
                if (ex is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax or AnonymousMethodExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax)
                {
                    fetcherExpr = ex;
                    break;
                }
            }
        }
        if (fetcherExpr is null) return;

        fetcherExpr = UnwrapCasts(fetcherExpr);

        // Case 1: bare method reference — `UseResource(FetchFoo, ...)` or
        // `UseResource(service.PostMessageAsync, ...)`.
        if (fetcherExpr is IdentifierNameSyntax bareId)
        {
            ReportIfNonIdempotent(context, bareId, bareId.Identifier.Text, methodName);
            return;
        }
        if (fetcherExpr is MemberAccessExpressionSyntax ma)
        {
            ReportIfNonIdempotent(context, ma.Name, ma.Name.Identifier.Text, methodName);
            return;
        }

        // Case 2: lambda body — walk invocations and flag the first offender.
        SyntaxNode? body = fetcherExpr switch
        {
            SimpleLambdaExpressionSyntax sl => sl.Body,
            ParenthesizedLambdaExpressionSyntax pl => pl.Body,
            AnonymousMethodExpressionSyntax am => am.Block,
            _ => null,
        };
        if (body is null) return;

        foreach (var call in body.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            var callName = GetInvokedMethodName(call);
            if (callName is null) continue;
            if (LooksNonIdempotent(callName))
            {
                ExpressionSyntax nameNode = call.Expression switch
                {
                    MemberAccessExpressionSyntax m => m.Name,
                    _ => call.Expression,
                };
                ReportIfNonIdempotent(context, nameNode, callName, methodName);
                return; // one diagnostic per hook call is enough
            }
        }
    }

    private static void ReportIfNonIdempotent(SyntaxNodeAnalysisContext context, SyntaxNode location, string calleeName, string hookName)
    {
        if (!LooksNonIdempotent(calleeName)) return;
        context.ReportDiagnostic(Diagnostic.Create(
            NonIdempotentFetcherRule,
            location.GetLocation(),
            hookName,
            StripAsyncSuffix(calleeName)));
    }

    private static bool LooksNonIdempotent(string name)
    {
        // Strip the `Async` suffix for matching — `PostAsync` should match the `Post`
        // prefix without that trailing noise.
        var stem = StripAsyncSuffix(name);
        foreach (var prefix in NonIdempotentPrefixes)
        {
            if (!stem.StartsWith(prefix)) continue;
            // Require a word boundary after the prefix so `PostalCode` doesn't
            // match `Post` and `Created` doesn't match `Create`. The next char
            // must be end-of-string or an upper-case letter (PascalCase word).
            if (stem.Length == prefix.Length) return true;
            var next = stem[prefix.Length];
            if (char.IsUpper(next)) return true;
        }
        return false;
    }

    private static string StripAsyncSuffix(string name)
        => name.EndsWith("Async") && name.Length > "Async".Length ? name.Substring(0, name.Length - "Async".Length) : name;
}
