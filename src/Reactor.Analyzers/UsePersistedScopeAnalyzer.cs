using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.UI.Reactor.Analyzers;

/// <summary>
/// <c>REACTOR_PERSIST_001</c> — flags a two-argument
/// <c>UsePersisted(key, initialValue)</c> call. That overload is
/// <c>=&gt; UsePersisted(key, initialValue, PersistedScope.Application)</c>
/// (<c>RenderContext.cs:824</c>), so the value is <b>process-wide</b> and bleeds
/// across windows/tabs that share the key — invisible until two windows are open
/// at once. The rule asks the author to state the scope explicitly.
/// </summary>
/// <remarks>
/// Detection is a cheap syntactic gate (name <c>UsePersisted</c>, exactly two
/// arguments, no explicit <c>scope:</c>) followed by a single semantic check that
/// the call really binds to the default-scope <c>UsePersisted&lt;T&gt;(string, T)</c>
/// hook — either on <c>RenderContext</c> (where it is declared) or on
/// <c>Component</c> (whose <c>protected</c> wrapper delegates to it, so an
/// unqualified <c>UsePersisted(...)</c> inside a component's <c>Render</c> is covered).
/// The three-argument overload, or any call on a same-named method that is not one
/// of those hook types, is left alone (spec 060 §4.8). The paired
/// <see cref="UsePersistedScopeCodeFix"/> offers <c>PersistedScope.Window</c>
/// (recommended) or <c>PersistedScope.Application</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UsePersistedScopeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTOR_PERSIST_001";

    private const string MethodName = "UsePersisted";
    private const string RenderContextTypeName = "RenderContext";
    private const string ComponentTypeName = "Component";
    private const string ReactorNamespacePrefix = "Microsoft.UI.Reactor";
    private const string ScopeParameterName = "scope";
    private const string KeyParameterName = "key";

    private static readonly LocalizableString Title =
        "UsePersisted defaults to Application (process-wide) scope";

    private static readonly LocalizableString MessageFormat =
        "UsePersisted({0}, …) defaults to PersistedScope.Application (process-wide). Specify PersistedScope.Window or PersistedScope.Application explicitly.";

    private static readonly LocalizableString Description =
        "The two-argument RenderContext.UsePersisted<T>(string, T) overload delegates to " +
        "PersistedScope.Application, so the persisted value lives for the whole process and " +
        "bleeds across windows or tabs that share the same key. This is invisible until two " +
        "windows are open at once. Call the three-argument overload and pass " +
        "PersistedScope.Window for host-scoped state, or PersistedScope.Application to make the " +
        "process-wide intent explicit (spec 033 §2 / spec 060 §4.8).";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        "Reactor.Persistence",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Syntactic gate #1 — the invoked simple name is UsePersisted. Handles
        // `ctx.UsePersisted(...)`, unqualified `UsePersisted(...)`, and the
        // explicit type-argument forms `ctx.UsePersisted<T>(...)` / `UsePersisted<T>(...)`.
        if (GetInvokedName(invocation) != MethodName)
            return;

        // Syntactic gate #2 — exactly two arguments (the shape of the default-scope overload).
        var args = invocation.ArgumentList.Arguments;
        if (args.Count != 2)
            return;

        // Syntactic gate #3 — the author has not already named a `scope:` argument.
        // (No valid call to the (string, T) overload names `scope`, but a same-named
        // overload that takes a scope could, and this keeps the fast path honest.)
        if (args.Any(static a => a.NameColon?.Name.Identifier.ValueText == ScopeParameterName))
            return;

        // Semantic confirmation — the call actually binds to the default-scope
        // UsePersisted overload on RenderContext (or Component's protected wrapper).
        // One GetSymbolInfo, gated behind the syntactic checks above (spec 060 §3).
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;
        if (!IsDefaultScopeUsePersistedOverload(method))
            return;

        // The diagnostic underlines the whole invocation (see location below); only the
        // message's {0} placeholder uses the key. A caller may reorder named arguments
        // (e.g. `UsePersisted(initialValue: x, key: k)`), so resolve an explicit `key:`
        // argument for the message, falling back to the first positional argument.
        var keyExpression = args.FirstOrDefault(static a => a.NameColon?.Name.Identifier.ValueText == KeyParameterName)?.Expression
            ?? args[0].Expression;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.GetLocation(),
            keyExpression.ToString()));
    }

    /// <summary>
    /// True when <paramref name="method"/> is the two-argument default-scope
    /// <c>UsePersisted&lt;T&gt;(string key, T initialValue)</c> hook — the overload that
    /// silently resolves to <c>PersistedScope.Application</c>. Recognized both on
    /// <c>RenderContext</c> (where it is declared) and on <c>Component</c> (whose
    /// <c>protected</c> wrapper delegates to it, <c>Component.cs:260</c>), because an
    /// unqualified <c>UsePersisted(...)</c> in a component's <c>Render</c> binds to the
    /// Component-declared method.
    /// </summary>
    private static bool IsDefaultScopeUsePersistedOverload(IMethodSymbol method)
    {
        if (method.Name != MethodName)
            return false;

        var containingType = method.ContainingType;
        if (containingType is null)
            return false;
        if (containingType.Name != RenderContextTypeName && containingType.Name != ComponentTypeName)
            return false;

        if (!IsReactorNamespace(containingType.ContainingNamespace?.ToDisplayString()))
            return false;

        // Target overload shape: (string key, T initialValue). Inspect the ORIGINAL
        // definition so a call that infers T = PersistedScope still matches (the
        // second parameter is the method's own type parameter), while the
        // three-argument overload (length 3) and any (string, PersistedScope) shape
        // (second parameter is the enum, not a type parameter) are excluded.
        if (method.Parameters.Length != 2)
            return false;

        var original = method.OriginalDefinition;
        if (original.Parameters[0].Type.SpecialType != SpecialType.System_String)
            return false;
        if (original.Parameters[1].Type.TypeKind != TypeKind.TypeParameter)
            return false;

        return true;
    }

    private static bool IsReactorNamespace(string? ns) =>
        ns is not null
            && (ns == ReactorNamespacePrefix
                || ns.StartsWith(ReactorNamespacePrefix + ".", StringComparison.Ordinal));

    private static string? GetInvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name switch
        {
            GenericNameSyntax gn => gn.Identifier.ValueText,
            { } simple => simple.Identifier.ValueText,
        },
        MemberBindingExpressionSyntax mb => mb.Name.Identifier.ValueText,
        _ => null,
    };
}
